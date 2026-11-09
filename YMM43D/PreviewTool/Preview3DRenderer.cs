using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Player;
using YMM43D.PreviewTool.Rendering;
using YMM43D.Project.Model;
using YMM43D.Project.Tachie;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class Preview3DRenderer : IDisposable
    {
        private static readonly Color4 BackgroundColor = new(0.15f, 0.15f, 0.15f, 1f);

        private const float MarkerGrabThreshold = 16f;

        private const byte PickAlphaThreshold = 8;

        private static readonly GizmoHandle[] MoveHandles = [GizmoHandle.MoveX, GizmoHandle.MoveY, GizmoHandle.MoveZ];

        private static readonly GizmoHandle[] PlaneHandles =
            [GizmoHandle.PlaneYZ, GizmoHandle.PlaneZX, GizmoHandle.PlaneXY];

        private static readonly GizmoHandle[] TurnHandles = [GizmoHandle.RotateX, GizmoHandle.RotateY, GizmoHandle.RotateZ];

        private static readonly GizmoHandle[] ScaleHandles = [GizmoHandle.ScaleX, GizmoHandle.ScaleY, GizmoHandle.ScaleZ];

        private static readonly GizmoHandle[] ScreenTurnHandle = [GizmoHandle.RotateZ];

        private readonly GridRenderer grid = new();
        private readonly CameraGizmoRenderer cameraGizmo = new();
        private readonly MarkerRenderer markers = new();
        private readonly TransformGizmoRenderer transformGizmo = new();

        private readonly BoneRenderer boneRenderer = new();
        private readonly OutlineRenderer outlineRenderer = new();
        private readonly MeshEditRenderer meshEditRenderer = new();
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
        private GizmoStyle lastGizmoStyle;
        private bool lastTurnsInThreeAxes;

        private IReadOnlyList<PreviewBone> lastBones = [];
        private PreviewOutline? lastOutline;
        private MeshEditTarget? lastMeshEdit;
        private (IVideoItem Item, FrameContext Time, Matrix4x4 World)? lastSingle;
        private Vector3? lastInflate;
        private Matrix4x4 lastViewProjection = Matrix4x4.Identity;
        private Vector3 lastViewPosition;
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
        private nint pickDeviceKey;

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
            DrawContext3D Context)
        {
            public Vector3 Origin => Vector3.Transform(Bounds.Center, World);
        }

        public void Prepare(PreviewScene scene)
        {
            contextBuilder.RetainOnly(scene.Items.Select(i => i.Item).ToHashSet());

            foreach (var previewItem in scene.Items)
            {
                contextBuilder.Prepare(
                    previewItem.Item,
                    previewItem.GetItemTime(scene.Time),
                    scene.Environment,
                    previewItem.Provider,
                    previewItem.Effects);
            }
        }

        public void Draw(
            ID3D11Device device,
            ID3D11DeviceContext context,
            ID3D11RenderTargetView renderTarget,
            ID3D11DepthStencilView depthStencil,
            int width,
            int height,
            PreviewScene scene)
        {
            var groups = GroupLookup.Build(
                scene.Environment.Scene?.Timeline, scene.Time.Frame, scene.Time.Fps);

            var entries = new List<(PreviewItem Item, DrawContext3D Context)>(scene.Items.Count);

            foreach (var previewItem in scene.Items)
            {
                var built = contextBuilder.Build(
                    previewItem.Item,
                    previewItem.GetItemTime(scene.Time),
                    scene.Environment,
                    previewItem.Provider,
                    groups);

                foreach (var drawContext in built)
                    entries.Add((previewItem, drawContext));
            }

            context.OMSetRenderTargets(renderTarget, depthStencil);
            context.ClearRenderTargetView(renderTarget, BackgroundColor);
            context.ClearDepthStencilView(depthStencil, DepthStencilClearFlags.Depth, 1f, 0);
            context.RSSetViewport(new Viewport(0, 0, width, height));

            var viewPose = scene.ViewPose;

            var pixelsPerTangent = SceneProjection.GetPixelsPerTangent(scene.SceneCamera, scene.ScreenHeight);

            var aspect = (float)width / Math.Max(1, height);

            var projection = scene.IsOrthographic
                ? SceneProjection.GetOrthographicMatrix(
                    aspect, scene.ScreenHeight, pixelsPerTangent, scene.ViewDistance)
                : SceneProjection.GetProjectionMatrix(aspect, scene.ScreenHeight, pixelsPerTangent);

            var casters = new List<SceneDepthCollector.Occluder>(entries.Count);

            foreach (var (previewItem, drawContext) in entries)
            {
                if (ReferenceEquals(previewItem.Provider, flatItemProvider))
                    continue;

                casters.Add(new SceneDepthCollector.Occluder(
                    previewItem.Provider, drawContext.World, drawContext.Time));
            }

            var lit = SceneShadows.Build(device, context, scene.Lighting, casters, this);

            var render = new Render3DContext(
                device, context, viewPose.ViewMatrix, projection, lit) { Scene = casters }.BindLights();

            var sceneCameraPose = scene.SceneCameraPose;
            var screenTangent = scene.GetScreenTangent(pixelsPerTangent);

            if (scene.ShowsGrid)
                grid.Draw(render, viewPose.Position);

            cameraGizmo.Draw(render, sceneCameraPose, screenTangent);

            frustumApex = sceneCameraPose.Position;
            frustumCorners = CameraFrustum.WorldCorners(sceneCameraPose, screenTangent);

            pickMarkers = scene.Markers;
            markers.Draw(render, scene.Markers, scene.SelectedMarker);

            try
            {
                foreach (var (previewItem, drawContext) in entries)
                {
                    RenderStates.IsMeshMode = scene.ShowsMesh && IsModel(previewItem.Item);

                    previewItem.Provider.Draw(render, drawContext);
                }
            }
            finally
            {
                RenderStates.IsMeshMode = false;
            }

            pickTargets =
            [
                .. entries.Select(entry => new PickTarget(
                    entry.Item.Item,
                    entry.Context.World,
                    GetLocalBounds(entry.Item, entry.Context.Time),
                    entry.Item.Provider,
                    entry.Context))
            ];

            gizmoMarker = FindGizmoMarker(scene);

            lastBones = ResolveBones(entries, scene);
            boneRenderer.Draw(render, lastBones, scene.SelectedBone);

            lastOutline = ResolveOutline(entries, scene);

            if (lastOutline is { } shownOutline)
                outlineRenderer.Draw(render, shownOutline.Points, viewPose.Position);

            lastSingle = SinglePlacement(entries, scene);
            lastMeshEdit = scene.IsEditingMesh ? ResolveMeshShape() : null;
            lastInflate = null;

            if (lastMeshEdit is { } shownMesh)
            {
                var (right, up) = LocalScreenAxes(shownMesh.ToWorld, viewPose);

                meshEditRenderer.Draw(
                    render,
                    shownMesh.ToWorld,
                    shownMesh.Mesh,
                    scene.MeshSelection,
                    scene.MeshPart,
                    scene.MeshRevision,
                    right,
                    up,
                    scene.CutEdge,
                    scene.ShowsInflateHandle);

                lastInflate = scene.ShowsInflateHandle
                    && MeshEditRenderer.HandleAt(shownMesh.Mesh, scene.MeshSelection, right) is { } grip
                        ? Vector3.Transform(grip, shownMesh.ToWorld)
                        : null;
            }

            var gizmoReach = scene.IsOrthographic
                ? (float?)scene.ViewDistance
                : null;

            lastTurnsInThreeAxes = false;

            lastGizmoStyle = gizmoMarker is not null
                ? GizmoStyle.Move
                : SelectedBone(scene) is not null
                    ? GizmoStyle.Turn
                    : TransformGizmo.StyleOf(scene.Mode);

            lastGizmo = !TransformGizmo.ShowsHandles(scene.Mode)
                ? null
                : gizmoMarker is { } placed
                ? Sized(placed.Marker.Position, viewPose.Position, gizmoReach)
                : SelectedBone(scene) is { } bone
                    ? Sized(
                        bone.Position, viewPose.Position, gizmoReach,
                        BoneOrientation(bone), TransformGizmo.BoneRatio)
                    : FindGizmo(scene, viewPose.Position, gizmoReach, lastGizmoStyle);

            if (SelectedBone(scene) is not null)
                lastTurnsInThreeAxes = true;

            if (lastGizmo is { } gizmo)
            {
                transformGizmo.Draw(
                    render, gizmo, scene.ActiveHandle, lastGizmoStyle, lastTurnsInThreeAxes);
            }

            axisIndicator.Draw(render, viewPose, width, height);

            lastViewProjection = viewPose.ViewMatrix * projection;
            lastViewPosition = viewPose.Position;
            lastView = viewPose.ViewMatrix;
            lastProjection = projection;
            lastLighting = scene.Lighting;
            lastDevice = device;
            lastContext = context;
            lastWidth = width;
            lastHeight = height;

            if (pendingPick is { } cursor)
            {
                pendingPick = null;
                pickResult = ResolvePick(cursor);
            }
        }

        private static (IVideoItem Item, FrameContext Time, Matrix4x4 World)? SinglePlacement(
            List<(PreviewItem Item, DrawContext3D Context)> entries, PreviewScene scene)
        {
            if (scene.Selection.Count != 1)
                return null;

            foreach (var selected in scene.Selection)
            {
                foreach (var (previewItem, context) in entries)
                {
                    if (ReferenceEquals(previewItem.Item, selected))
                        return (selected, context.Time, context.World);
                }
            }

            return null;
        }

        public MeshEditTarget? ResolveMeshShape()
            => lastSingle is { } placed
                ? MeshEditTarget.Resolve(placed.Item, placed.Time, placed.World)
                : null;

        private static (Vector3 Right, Vector3 Up) LocalScreenAxes(in Matrix4x4 toWorld, in CameraPose pose)
        {
            if (!Matrix4x4.Invert(pose.ViewMatrix, out var camera)
                || !Matrix4x4.Invert(toWorld, out var back))
            {
                return (Vector3.UnitX, Vector3.UnitY);
            }

            var right = Vector3.TransformNormal(new Vector3(camera.M11, camera.M12, camera.M13), back);
            var up = Vector3.TransformNormal(new Vector3(camera.M21, camera.M22, camera.M23), back);

            return (Straight(right, Vector3.UnitX), Straight(up, Vector3.UnitY));
        }

        public MeshEditTarget? MeshEdit => lastMeshEdit;

        public Vector3? InflateHandle => lastInflate;

        public bool PickInflate(Vector2 position)
            => lastInflate is { } grip
            && ToScreen(grip) is { } at
            && Vector2.Distance(position, at) < MeshEditRenderer.GrabThreshold * 2f;

        public int PickMeshVertex(Vector2 position)
        {
            if (lastMeshEdit is not { } edit)
                return -1;

            var nearest = MeshEditRenderer.GrabThreshold;
            var found = -1;

            for (var i = 0; i < edit.Points.Length; i++)
            {
                if (ToScreen(edit.Points[i]) is not { } at)
                    continue;

                var distance = Vector2.Distance(position, at);

                if (distance >= nearest)
                    continue;

                nearest = distance;
                found = i;
            }

            return found;
        }

        public int PickMeshEdge(Vector2 position)
        {
            if (lastMeshEdit is not { } edit)
                return -1;

            var nearest = MeshEditRenderer.GrabThreshold;
            var found = -1;

            for (var i = 0; i < edit.Mesh.Edges.Length; i++)
            {
                var edge = edit.Mesh.Edges[i];

                if (ToScreen(edit.Points[edge.A]) is not { } from
                    || ToScreen(edit.Points[edge.B]) is not { } to)
                {
                    continue;
                }

                var distance = DistanceToSegment(position, from, to);

                if (distance >= nearest)
                    continue;

                nearest = distance;
                found = i;
            }

            return found;
        }

        public int PickMeshFace(Vector2 position)
        {
            if (lastMeshEdit is not { } edit)
                return -1;

            var nearest = float.MaxValue;
            var found = -1;

            for (var f = 0; f < edit.Mesh.Faces.Length; f++)
            {
                var corners = edit.Mesh.Faces[f];
                var flat = new Vector2[corners.Length];
                var middle = Vector3.Zero;
                var drawn = true;

                for (var i = 0; i < corners.Length; i++)
                {
                    if (ToScreen(edit.Points[corners[i]]) is not { } at)
                    {
                        drawn = false;
                        break;
                    }

                    flat[i] = at;
                    middle += edit.Points[corners[i]];
                }

                if (!drawn || !IsInsidePolygon(position, flat))
                    continue;

                var depth = Vector3.Distance(middle / corners.Length, lastViewPosition);

                if (depth >= nearest)
                    continue;

                nearest = depth;
                found = f;
            }

            return found;
        }

        private static bool IsInsidePolygon(Vector2 point, Vector2[] corners)
        {
            for (var i = 1; i + 1 < corners.Length; i++)
            {
                if (IsInsideTriangle(point, corners[0], corners[i], corners[i + 1]))
                    return true;
            }

            return false;
        }

        private static PreviewOutline? ResolveOutline(
            List<(PreviewItem Item, DrawContext3D Context)> entries, PreviewScene scene)
        {
            if (scene.Selection.Count != 1)
                return null;

            foreach (var selected in scene.Selection)
            {
                foreach (var (previewItem, context) in entries)
                {
                    if (!ReferenceEquals(previewItem.Item, selected))
                        continue;

                    if (PreviewOutline.Resolve(selected, context.Time, context.World) is { } found)
                        return found;
                }
            }

            return null;
        }

        public PreviewOutline? Outline => lastOutline;

        public int PickOutlinePoint(Vector2 position)
        {
            if (lastOutline is not { } outline)
                return -1;

            var nearest = OutlineRenderer.GrabThreshold;
            var found = -1;

            for (var i = 0; i < outline.Points.Length; i++)
            {
                if (ToScreen(outline.Points[i]) is not { } at)
                    continue;

                var distance = Vector2.Distance(position, at);

                if (distance >= nearest)
                    continue;

                nearest = distance;
                found = i;
            }

            return found;
        }

        private static IReadOnlyList<PreviewBone> ResolveBones(
            List<(PreviewItem Item, DrawContext3D Context)> entries, PreviewScene scene)
        {
            foreach (var selected in scene.Selection)
            {
                foreach (var (previewItem, context) in entries)
                {
                    if (!ReferenceEquals(previewItem.Item, selected))
                        continue;

                    if (BoneOverlay.Resolve(selected, context.Time, context.World) is { Count: > 0 } bones)
                        return bones;
                }
            }

            return [];
        }

        private static bool IsModel(IVideoItem item)
            => item is ShapeItem { ShapeParameter: Model3DParameter }
            or TachieItem { Character.TachieCharacterParameter: Model3DCharacterParameter };

        private PreviewBone? SelectedBone(PreviewScene scene)
            => scene.SelectedBone >= 0 && scene.SelectedBone < lastBones.Count
                ? lastBones[scene.SelectedBone]
                : null;

        private static Matrix4x4 BoneOrientation(in PreviewBone bone)
        {
            return bone.HandleOrientation(0f, 0f, 0f);
        }

        public IReadOnlyList<PreviewBone> Bones => lastBones;

        public int PickBone(Vector2 position)
        {
            var nearest = float.MaxValue;
            var nearestHead = float.MaxValue;
            var found = -1;

            for (var i = 0; i < lastBones.Count; i++)
            {
                if (ToScreen(lastBones[i].Position) is not { } head)
                    continue;

                var distance = DistanceToSegment(position, head, ToScreen(lastBones[i].Tail) ?? head);

                if (distance >= BoneGrabThreshold)
                    continue;

                var toHead = Vector2.Distance(position, head);

                var better = found < 0
                    || distance < nearest - BoneTieBreak
                    || (distance < nearest + BoneTieBreak
                        && (lastBones[i].HasChildren != lastBones[found].HasChildren
                            ? lastBones[i].HasChildren
                            : toHead < nearestHead));

                if (!better)
                    continue;

                found = i;
                nearest = MathF.Min(nearest, distance);
                nearestHead = toHead;
            }

            return found;
        }

        public const float BoneGrabThreshold = 10f;

        private const float BoneTieBreak = 4f;

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

        private static TransformGizmo Sized(
            in Vector3 origin,
            in Vector3 cameraPosition,
            float? reach,
            in Matrix4x4? orientation = null,
            float ratio = 1f)
            => reach is { } distance
                ? TransformGizmo.AtDistance(origin, distance, orientation, ratio)
                : TransformGizmo.Create(origin, cameraPosition, orientation, ratio);

        private TransformGizmo? FindGizmo(
            PreviewScene scene, in Vector3 cameraPosition, float? reach, GizmoStyle style)
        {
            if (scene.IsEditingMesh)
            {
                if (lastMeshEdit is not { } edit || scene.MeshSelection.Count == 0)
                    return null;

                lastTurnsInThreeAxes = true;

                var middle = Vector3.Transform(edit.Mesh.Middle([.. scene.MeshSelection]), edit.ToWorld);

                return Sized(middle, cameraPosition, reach, Facing(edit.ToWorld));
            }

            var sum = Vector3.Zero;
            var count = 0;
            IVideoItem? only = null;

            foreach (var target in pickTargets)
            {
                if (target.Item.IsLocked || !scene.Selection.Contains(target.Item))
                    continue;

                if (count == 0)
                    only = target.Item;
                else if (!ReferenceEquals(only, target.Item))
                    only = null;

                sum += target.Origin;
                count++;
            }

            if (count == 0)
                return null;

            var origin = sum / count;

            if (only is not { } item || ItemRotation.Find(item) is not { } rotation)
                return Sized(origin, cameraPosition, reach);

            lastTurnsInThreeAxes = true;

            var time = FrameContext.ForItem(item, scene.Time.Frame, scene.Time.Fps);

            var facing = rotation.ToOrientation(
                    rotation.RotationX.GetFloat(time),
                    rotation.RotationY.GetFloat(time),
                    rotation.RotationZ.GetFloat(time))
                * Matrix4x4.CreateRotationZ(-float.DegreesToRadians(item.Rotation.GetFloat(time)));

            var alongObject = scene.Space is GizmoSpace.Local || style is GizmoStyle.Scale;

            return Sized(origin, cameraPosition, reach, alongObject ? facing : null);
        }

        private static Matrix4x4 Facing(in Matrix4x4 world)
        {
            var x = Straight(new Vector3(world.M11, world.M12, world.M13), Vector3.UnitX);
            var y = Straight(new Vector3(world.M21, world.M22, world.M23), Vector3.UnitY);
            var z = Straight(new Vector3(world.M31, world.M32, world.M33), Vector3.UnitZ);

            return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);
        }

        private static Vector3 Straight(in Vector3 value, in Vector3 fallback)
            => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : fallback;

        public IReadOnlyList<IVideoItem> VisibleItems => [.. pickTargets.Select(target => target.Item).Distinct()];

        public PickTarget? FindTarget(IVideoItem item)
        {
            foreach (var target in pickTargets)
            {
                if (target.Item == item)
                    return target;
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

        public ViewDirection? PickAxisIndicator(Vector2 position) => axisIndicator.Pick(position);

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

            if (lastGizmoStyle is GizmoStyle.Move)
            {
                foreach (var handle in MoveHandles)
                {
                    if (ToScreen(gizmo.AxisEnd(handle)) is not { } end)
                        continue;

                    var distance = DistanceToSegment(position, origin, end);
                    if (distance >= nearest)
                        continue;

                    nearest = distance;
                    found = handle;
                }

                foreach (var handle in PlaneHandles)
                {
                    if (!IsInsidePlane(gizmo, handle, position))
                        continue;

                    nearest = 0f;
                    found = handle;
                }
            }

            if (lastGizmoStyle is GizmoStyle.Scale)
            {
                foreach (var handle in ScaleHandles)
                {
                    if (ToScreen(gizmo.ScaleEnd(handle)) is not { } end)
                        continue;

                    var distance = DistanceToSegment(position, origin, end);
                    if (distance >= nearest)
                        continue;

                    nearest = distance;
                    found = handle;
                }

                if (Vector2.Distance(position, origin) <= TransformGizmo.CenterGrab)
                {
                    nearest = 0f;
                    found = GizmoHandle.ScaleAll;
                }
            }

            var rings = lastGizmoStyle switch
            {
                GizmoStyle.Turn => lastTurnsInThreeAxes ? TurnHandles : ScreenTurnHandle,
                _ => [],
            };

            foreach (var handle in rings)
            {
                for (var i = 0; i < TransformGizmo.RingSegments; i++)
                {
                    if (ToScreen(gizmo.RingPoint(handle, i)) is not { } from
                        || ToScreen(gizmo.RingPoint(handle, i + 1)) is not { } to)
                    {
                        continue;
                    }

                    var distance = DistanceToSegment(position, from, to);
                    if (distance >= nearest)
                        continue;

                    nearest = distance;
                    found = handle;
                }
            }

            return found;
        }

        private bool IsInsidePlane(in TransformGizmo gizmo, GizmoHandle handle, Vector2 position)
        {
            Span<Vector2> corners = stackalloc Vector2[TransformGizmo.PlaneCorners];

            for (var i = 0; i < TransformGizmo.PlaneCorners; i++)
            {
                if (ToScreen(gizmo.PlanePoint(handle, i)) is not { } corner)
                    return false;

                corners[i] = corner;
            }

            return IsInsideTriangle(position, corners[0], corners[1], corners[2])
                || IsInsideTriangle(position, corners[0], corners[2], corners[3]);
        }

        private static bool IsInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var first = Side(point, a, b);
            var second = Side(point, b, c);
            var third = Side(point, c, a);

            return (first >= 0f && second >= 0f && third >= 0f)
                || (first <= 0f && second <= 0f && third <= 0f);
        }

        private static float Side(Vector2 point, Vector2 from, Vector2 to)
        {
            var edge = to - from;
            var offset = point - from;

            return edge.X * offset.Y - edge.Y * offset.X;
        }

        public WorldBounds? GetBounds(IReadOnlyCollection<IVideoItem>? items)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var found = false;

            foreach (var target in pickTargets)
            {
                if (items is not null && !items.Contains(target.Item))
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

                var render = new Render3DContext(
                    device, context, lastView, lastProjection, lastLighting).BindLights();

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
            if (pickSurface is not null && pickDeviceKey == device.NativePointer)
                return;

            ReleasePickSurface();

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
            pickDeviceKey = device.NativePointer;
        }

        private void ReleasePickSurface()
        {
            pickView?.Dispose();
            pickView = null;
            pickStaging?.Dispose();
            pickStaging = null;
            pickSurface?.Dispose();
            pickSurface = null;
            pickDeviceKey = nint.Zero;
        }

        public void Dispose()
        {
            ReleasePickSurface();

            grid.Dispose();
            cameraGizmo.Dispose();
            boneRenderer.Dispose();
            markers.Dispose();
            outlineRenderer.Dispose();
            meshEditRenderer.Dispose();
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

        public IReadOnlyCollection<IVideoItem> Selection { get; init; } = [];

        public int SelectedBone { get; init; } = -1;

        public bool ShowsMesh { get; init; }

        public IReadOnlyList<SceneMarkerResolver.PlacedMarker> Markers { get; init; } = [];

        public IItem? SelectedMarker { get; init; }

        public bool ShowsGrid { get; init; } = true;

        public GizmoHandle ActiveHandle { get; init; }

        public bool IsOrthographic { get; init; }

        public bool IsEditingMesh { get; init; }

        public MeshPart MeshPart { get; init; }

        public IReadOnlySet<int> MeshSelection { get; init; } = new HashSet<int>();

        public long MeshRevision { get; init; }

        public int CutEdge { get; init; } = -1;

        public bool ShowsInflateHandle { get; init; }

        public float ViewDistance { get; init; } = SceneProjection.DefaultFocalDistance;

        public GizmoSpace Space { get; init; }

        public GizmoMode Mode { get; init; }
    }
}
