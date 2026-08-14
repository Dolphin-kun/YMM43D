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

        private const byte PickAlphaThreshold = 8;

        private readonly GridRenderer grid = new();
        private readonly CameraGizmoRenderer cameraGizmo = new();
        private readonly MarkerRenderer markers = new();
        private readonly TransformGizmoRenderer transformGizmo = new();
        private readonly AxisIndicatorRenderer axisIndicator = new();
        private readonly FlatItemProvider flatItemProvider = new();
        private readonly ItemDrawContextBuilder contextBuilder = new();

        private PickTarget[] pickTargets = [];
        private IReadOnlyList<SceneMarkerResolver.PlacedMarker> pickMarkers = [];
        private SceneMarkerResolver.PlacedMarker? gizmoMarker;
        private Vector2? pendingPick;
        private PickTarget? pickResult;
        private Vector3 frustumApex;
        private Vector3[] frustumCorners = [];
        private TransformGizmo? lastGizmo;
        private Matrix4x4 lastViewProjection = Matrix4x4.Identity;
        private Matrix4x4 lastView = Matrix4x4.Identity;
        private Matrix4x4 lastProjection = Matrix4x4.Identity;
        private SceneLighting? lastLighting;
        private ID3D11Device? lastDevice;
        private ID3D11DeviceContext? lastContext;
        private float lastWidth;
        private float lastHeight;

        private ID3D11Texture2D? pickSurface;
        private ID3D11RenderTargetView? pickView;
        private ID3D11Texture2D? pickStaging;

        public I3DProvider DefaultProvider => flatItemProvider;

        public void ResetItemCaches()
        {
            contextBuilder.Reset();
            pickTargets = [];
        }

        internal readonly record struct PickTarget(
            IVideoItem Item,
            Matrix4x4 World,
            WorldBounds Bounds,
            I3DProvider Provider,
            DrawContext3D Context);

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

            var sceneCameraPose = scene.SceneCameraPose;
            var screenTangent = scene.GetScreenTangent(pixelsPerTangent);

            if (scene.ShowsGrid)
                grid.Draw(render, viewPose.Position);

            cameraGizmo.Draw(render, sceneCameraPose, screenTangent);

            frustumApex = sceneCameraPose.Position;
            frustumCorners = CameraFrustum.WorldCorners(sceneCameraPose, screenTangent);

            pickMarkers = scene.Markers;
            markers.Draw(render, scene.Markers, scene.SelectedMarker);

            for (var i = 0; i < scene.Items.Count; i++)
                scene.Items[i].Provider.Draw(render, drawContexts[i]);

            pickTargets =
            [
                .. scene.Items.Select((item, i) => new PickTarget(
                    item.Item,
                    drawContexts[i].World,
                    GetLocalBounds(item, drawContexts[i].Time),
                    item.Provider,
                    drawContexts[i]))
            ];

            gizmoMarker = FindGizmoMarker(scene);

            lastGizmo = gizmoMarker is { } placed
                ? TransformGizmo.Create(placed.Marker.Position, viewPose.Position)
                : FindGizmo(scene.Selected, viewPose.Position);

            if (lastGizmo is { } gizmo)
                transformGizmo.Draw(render, gizmo, scene.ActiveHandle, gizmoMarker is null);

            axisIndicator.Draw(render, viewPose, width, height);

            lastViewProjection = viewPose.ViewMatrix * projection;
            lastView = viewPose.ViewMatrix;
            lastProjection = projection;
            lastLighting = scene.Lighting;
            lastDevice = device;
            lastContext = context;
            lastWidth = width;
            lastHeight = height;

            // 掴む判定はここで済ませる。アイテムをもう一度描くので、いま描いたばかりの
            // この場所――描画側と同じ鍵の内側――でなければ、デバイスを別の場所から
            // 同時に触ることになる。
            if (pendingPick is { } cursor)
            {
                pendingPick = null;
                pickResult = ResolvePick(cursor);
            }
        }

        public void RequestPick(Vector2 position)
        {
            pendingPick = position;
            pickResult = null;
        }

        public PickTarget? TakePickResult()
        {
            var found = pickResult;

            pickResult = null;
            pendingPick = null;

            return found;
        }

        private static WorldBounds GetLocalBounds(PreviewItem item, in FrameContext itemTime)
        {
            if (item.Provider is not I3DBounds provider)
                return WorldBounds.FromCube(1f);

            var bounds = provider.GetLocalBounds(itemTime);

            return bounds.IsEmpty ? WorldBounds.FromCube(1f) : bounds;
        }

        private static SceneMarkerResolver.PlacedMarker? FindGizmoMarker(PreviewScene scene)
        {
            if (scene.SelectedMarker is not { } item)
                return null;

            foreach (var placed in scene.Markers)
            {
                if (ReferenceEquals(placed.Item, item))
                    return placed;
            }

            return null;
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

            return found ?? PickCameraFrustum(position);
        }

        private SceneMarkerResolver.PlacedMarker? PickCameraFrustum(Vector2 position)
        {
            if (!IsInsideFrustum(position))
                return null;

            foreach (var placed in pickMarkers)
            {
                if (placed.Marker.Kind == MarkerKind.Camera
                    && Vector3.DistanceSquared(placed.Marker.Position, frustumApex) < 1e-8f)
                {
                    return placed;
                }
            }

            return null;
        }

        private bool IsInsideFrustum(in Vector2 position)
        {
            if (frustumCorners.Length != 4 || ToScreen(frustumApex) is not { } apex)
                return false;

            var corners = new Vector2[4];

            for (var i = 0; i < 4; i++)
            {
                if (ToScreen(frustumCorners[i]) is not { } spot)
                    return false;

                corners[i] = spot;
            }

            for (var i = 0; i < 4; i++)
            {
                var next = corners[(i + 1) % 4];

                if (IsInsideTriangle(position, apex, corners[i], next)
                    || IsInsideTriangle(position, corners[0], corners[i], next))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInsideTriangle(in Vector2 point, in Vector2 a, in Vector2 b, in Vector2 c)
        {
            static float Side(in Vector2 from, in Vector2 to, in Vector2 point)
                => (to.X - from.X) * (point.Y - from.Y) - (to.Y - from.Y) * (point.X - from.X);

            var ab = Side(a, b, point);
            var bc = Side(b, c, point);
            var ca = Side(c, a, point);

            return (ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f);
        }

        public PickRay? CreateRay(Vector2 position)
            => PickRay.FromScreen(position, lastWidth, lastHeight, lastViewProjection);

        public TransformGizmo? Gizmo => lastGizmo;

        public SceneMarkerResolver.PlacedMarker? GizmoMarker => gizmoMarker;

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

            if (gizmoMarker is not null)
                return found;

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

        private PickTarget? ResolvePick(Vector2 position)
        {
            if (CreateRay(position) is not { } cast)
                return null;

            var hits = new List<(float Distance, PickTarget Target)>();

            foreach (var target in pickTargets)
            {
                if (target.Item.IsLocked)
                    continue;

                if (cast.IntersectBox(target.Bounds, target.World) is { } distance)
                    hits.Add((distance, target));
            }

            hits.Sort((left, right) => left.Distance.CompareTo(right.Distance));

            foreach (var (_, target) in hits)
            {
                if (DrawsAt(position, target))
                    return target;
            }

            return null;
        }

        private bool DrawsAt(in Vector2 position, in PickTarget target)
        {
            if (lastDevice is not { } device || lastContext is not { } context
                || lastWidth <= 0f || lastHeight <= 0f)
            {
                return true;
            }

            try
            {
                EnsurePickSurface(device);

                if (pickView is not { } view || pickSurface is not { } surface || pickStaging is not { } staging)
                    return true;

                context.OMSetRenderTargets(view);
                context.ClearRenderTargetView(view, new Color4(0f, 0f, 0f, 0f));

                context.RSSetViewport(new Viewport(-position.X, -position.Y, lastWidth, lastHeight));

                var render = new Render3DContext(device, context, lastView, lastProjection, lastLighting);

                target.Provider.Draw(render, AsOpaque(target.Context));

                context.OMSetRenderTargets([], null);
                context.CopyResource(staging, surface);

                var map = context.Map(staging, 0, MapMode.Read);

                try
                {
                    unsafe
                    {
                        return ((byte*)map.DataPointer)[3] >= PickAlphaThreshold;
                    }
                }
                finally
                {
                    context.Unmap(staging, 0);
                }
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static DrawContext3D AsOpaque(DrawContext3D context) => new()
        {
            World = context.World,
            Opacity = 1f,
            Blend = Graphics.BlendMode.Normal,
            IsAlwaysOnTop = context.IsAlwaysOnTop,
            Time = context.Time,
            Texture = context.Texture,
        };

        private void EnsurePickSurface(ID3D11Device device)
        {
            if (pickSurface is not null)
                return;

            var description = new Texture2DDescription
            {
                Width = 1,
                Height = 1,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.R8G8B8A8_UNorm,
                SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
            };

            pickSurface = device.CreateTexture2D(description);
            pickView = device.CreateRenderTargetView(pickSurface);
            pickStaging = device.CreateTexture2D(description with
            {
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
            });
        }

        public void Dispose()
        {
            pickView?.Dispose();
            pickView = null;
            pickStaging?.Dispose();
            pickStaging = null;
            pickSurface?.Dispose();
            pickSurface = null;

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

        public bool ShowsGrid { get; init; } = true;

        public GizmoHandle ActiveHandle { get; init; }
    }
}
