using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Player;
using YMM43D.Plugin;
using YMM43D.PreviewTool.Rendering;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class Preview3DRenderer : IDisposable
    {
        private static readonly Color4 BackgroundColor = new(0.15f, 0.15f, 0.15f, 1f);

        private const float MarkerGrabThreshold = 16f;

        private readonly GridRenderer grid = new();
        private readonly CameraGizmoRenderer cameraGizmo = new();
        private readonly MarkerRenderer markers = new();
        private readonly TransformGizmoRenderer transformGizmo = new();
        private readonly AxisIndicatorRenderer axisIndicator = new();
        private readonly FlatItemProvider flatItemProvider = new();
        private readonly ItemDrawContextBuilder contextBuilder = new();

        private PickTarget[] pickTargets = [];
        private IReadOnlyList<SceneMarkerResolver.PlacedMarker> pickMarkers = [];
        private TransformGizmo? lastGizmo;
        private Matrix4x4 lastViewProjection = Matrix4x4.Identity;
        private float lastWidth;
        private float lastHeight;

        public I3DProvider DefaultProvider => flatItemProvider;

        internal readonly record struct PickTarget(IVideoItem Item, Matrix4x4 World, WorldBounds Bounds);

        public void Draw(
            ID3D11Device device,
            ID3D11DeviceContext context,
            ID3D11RenderTargetView renderTarget,
            ID3D11DepthStencilView depthStencil,
            int width,
            int height,
            PreviewScene scene)
        {
            var drawContexts = new DrawContext3D[scene.Items.Count];
            for (var i = 0; i < scene.Items.Count; i++)
            {
                var previewItem = scene.Items[i];
                drawContexts[i] = contextBuilder.Build(
                    previewItem.Item,
                    previewItem.GetItemTime(scene.Time),
                    scene.Environment,
                    previewItem.Provider);
            }

            contextBuilder.RetainOnly(scene.Items.Select(i => i.Item).ToHashSet());

            context.OMSetRenderTargets(renderTarget, depthStencil);
            context.ClearRenderTargetView(renderTarget, BackgroundColor);
            context.ClearDepthStencilView(depthStencil, DepthStencilClearFlags.Depth, 1f, 0);
            context.RSSetViewport(new Viewport(0, 0, width, height));

            var viewPose = scene.ViewPose;

            var pixelsPerTangent = SceneProjection.GetPixelsPerTangent(scene.SceneCamera, scene.ScreenHeight);

            var projection = SceneProjection.GetProjectionMatrix(
                (float)width / Math.Max(1, height), scene.ScreenHeight, pixelsPerTangent);

            var render = new Render3DContext(
                device, context, viewPose.ViewMatrix, projection, scene.Lighting);

            grid.Draw(render, viewPose.Position);
            cameraGizmo.Draw(render, scene.SceneCameraPose, scene.GetScreenTangent(pixelsPerTangent));

            pickMarkers = scene.Markers;
            markers.Draw(render, scene.Markers, scene.SelectedMarker);

            for (var i = 0; i < scene.Items.Count; i++)
                scene.Items[i].Provider.Draw(render, drawContexts[i]);

            pickTargets =
            [
                .. scene.Items.Select((item, i) => new PickTarget(
                    item.Item, drawContexts[i].World, GetLocalBounds(item, drawContexts[i].Time)))
            ];

            lastGizmo = FindGizmo(scene.Selected, viewPose.Position);
            if (lastGizmo is { } gizmo)
                transformGizmo.Draw(render, gizmo, scene.ActiveHandle);

            axisIndicator.Draw(render, viewPose, width, height);

            lastViewProjection = viewPose.ViewMatrix * projection;
            lastWidth = width;
            lastHeight = height;
        }

        private static WorldBounds GetLocalBounds(PreviewItem item, in FrameContext itemTime)
        {
            if (item.Provider is not I3DBounds provider)
                return WorldBounds.FromCube(1f);

            var bounds = provider.GetLocalBounds(itemTime);

            return bounds.IsEmpty ? WorldBounds.FromCube(1f) : bounds;
        }

        private TransformGizmo? FindGizmo(IVideoItem? selected, in Vector3 cameraPosition)
        {
            if (selected is null || selected.IsLocked)
                return null;

            foreach (var target in pickTargets)
            {
                if (target.Item == selected)
                    return TransformGizmo.Create(target.World.Translation, cameraPosition);
            }

            return null;
        }

        public SceneMarkerResolver.PlacedMarker? PickMarker(Vector2 position)
        {
            SceneMarkerResolver.PlacedMarker? found = null;
            var nearest = MarkerGrabThreshold;

            foreach (var placed in pickMarkers)
            {
                if (ToScreen(placed.Marker.Position) is not { } spot)
                    continue;

                var distance = Vector2.Distance(position, spot);
                if (distance >= nearest)
                    continue;

                nearest = distance;
                found = placed;
            }

            return found;
        }

        public PickRay? CreateRay(Vector2 position)
            => PickRay.FromScreen(position, lastWidth, lastHeight, lastViewProjection);

        public TransformGizmo? Gizmo => lastGizmo;

        public GizmoHandle PickGizmo(Vector2 position)
        {
            if (lastGizmo is not { } gizmo)
                return GizmoHandle.None;

            if (ToScreen(gizmo.Origin) is not { } origin)
                return GizmoHandle.None;

            var found = GizmoHandle.None;
            var nearest = TransformGizmo.GrabThreshold;

            foreach (var handle in new[] { GizmoHandle.MoveX, GizmoHandle.MoveY, GizmoHandle.MoveZ })
            {
                if (ToScreen(gizmo.AxisEnd(handle)) is not { } end)
                    continue;

                var distance = DistanceToSegment(position, origin, end);
                if (distance >= nearest)
                    continue;

                nearest = distance;
                found = handle;
            }

            for (var i = 0; i < TransformGizmo.RingSegments; i++)
            {
                if (ToScreen(gizmo.RingPoint(i)) is not { } from
                    || ToScreen(gizmo.RingPoint(i + 1)) is not { } to)
                {
                    continue;
                }

                var distance = DistanceToSegment(position, from, to);
                if (distance >= nearest)
                    continue;

                nearest = distance;
                found = GizmoHandle.RotateZ;
            }

            return found;
        }

        public WorldBounds? GetBounds(IVideoItem? item)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var found = false;

            foreach (var target in pickTargets)
            {
                if (item is not null && target.Item != item)
                    continue;

                var box = target.Bounds.Transform(target.World);

                min = Vector3.Min(min, box.Min);
                max = Vector3.Max(max, box.Max);
                found = true;
            }

            return found ? new WorldBounds(min, max) : null;
        }

        public Vector2? ToScreen(in Vector3 point)
        {
            if (lastWidth <= 0f || lastHeight <= 0f)
                return null;

            var clip = Vector4.Transform(new Vector4(point, 1f), lastViewProjection);

            if (clip.W <= 1e-6f)
                return null;

            var ndc = new Vector2(clip.X, clip.Y) / clip.W;

            return new Vector2((ndc.X + 1f) * lastWidth / 2f, (1f - ndc.Y) * lastHeight / 2f);
        }

        private static float DistanceToSegment(in Vector2 point, in Vector2 from, in Vector2 to)
        {
            var span = to - from;
            var lengthSquared = span.LengthSquared();

            if (lengthSquared < 1e-6f)
                return Vector2.Distance(point, from);

            var rate = Math.Clamp(Vector2.Dot(point - from, span) / lengthSquared, 0f, 1f);

            return Vector2.Distance(point, from + span * rate);
        }

        public PickTarget? Pick(Vector2 position, out PickRay ray)
        {
            ray = default;

            if (CreateRay(position) is not { } cast)
                return null;

            ray = cast;

            PickTarget? found = null;
            var nearest = float.PositiveInfinity;

            foreach (var target in pickTargets)
            {
                if (target.Item.IsLocked)
                    continue;

                if (cast.IntersectBox(target.Bounds, target.World) is not { } distance || distance >= nearest)
                    continue;

                nearest = distance;
                found = target;
            }

            return found;
        }

        public void Dispose()
        {
            grid.Dispose();
            cameraGizmo.Dispose();
            markers.Dispose();
            transformGizmo.Dispose();
            axisIndicator.Dispose();
            flatItemProvider.Dispose();
            contextBuilder.Dispose();
        }
    }

    internal sealed class PreviewScene
    {
        public required CameraPose ViewPose { get; init; }

        public required CameraState SceneCamera { get; init; }

        public SceneLighting Lighting { get; init; } = SceneLighting.Default;

        public CameraPose SceneCameraPose => SceneCamera.GetPose();

        public float ScreenWidth
            => Environment.SourceDescription?.ScreenSize.Width ?? 0f;

        public float ScreenHeight
            => Environment.SourceDescription?.ScreenSize.Height ?? 0f;

        public Vector2 GetScreenTangent(float pixelsPerTangent)
        {
            if (ScreenWidth <= 0f || ScreenHeight <= 0f || pixelsPerTangent <= 0f)
                return new Vector2(0.96f, 0.54f);

            return new Vector2(ScreenWidth, ScreenHeight) / (2f * pixelsPerTangent);
        }

        public required FrameContext Time { get; init; }

        public required PreviewEnvironment Environment { get; init; }

        public required IReadOnlyList<PreviewItem> Items { get; init; }

        public IVideoItem? Selected { get; init; }

        public IReadOnlyList<SceneMarkerResolver.PlacedMarker> Markers { get; init; } = [];

        public IItem? SelectedMarker { get; init; }

        public GizmoHandle ActiveHandle { get; init; }
    }
}
