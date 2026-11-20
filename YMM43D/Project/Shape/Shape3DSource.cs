using System.Numerics;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Commons;
using YMM43D.Project.Shape.Modifiers;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape
{
    internal sealed class Shape3DSource(IGraphicsDevicesAndContext devices, Shape3DParameter parameter)
        : Shape3DSourceBase(devices), I3DGeometrySource, I3DSnapshot
    {
        private readonly Shape3DParameter parameter = parameter;
        private readonly DeviceResourceCache<SolidResources> resources = new(device => new SolidResources(device));
        private readonly ShapeMaterialPainter painter = new();

        private Built? built;

        public override void Draw(in Render3DContext render, DrawContext3D item)
            => Paint(render, item, GetLocalMatrix(item.Time), (render.IsOutput ? null : parameter.PreviewGeometry(item.Time)) ?? Geometry(item.Time));

        public I3DProvider Snapshot(in FrameContext time) => new Frozen(this, Geometry(time), GetLocalMatrix(time));

        private void Paint(in Render3DContext render, DrawContext3D item, Matrix4x4 local, SurfaceGeometry geometry)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            var world = local * item.World;

            if (geometry.Faces.Length == 0)
                return;

            if (parameter.ShownMaterials is { Count: > 0 } materials)
            {
                painter.Draw(render, item, world, geometry, materials, parameter.IsUnlit);
                return;
            }

            var constants = render.CreateConstants(world, item.Opacity, parameter.IsUnlit, item.AlphaCutoff, parameter.GetGloss(item.Time));
            var mesh = shared.GetMesh(geometry, parameter.ResolvedColors);

            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Front), mesh);
            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Back), mesh);
        }

        public bool TryGetGeometry(
            in FrameContext itemTime,
            out SurfaceGeometry geometry,
            out IReadOnlyList<Vortice.Mathematics.Color4> colors,
            out Matrix4x4 local)
        {
            geometry = Geometry(itemTime);
            colors = parameter.ShownMaterials is { Count: > 0 } materials
                ? [.. materials.Select(material => material.Color.ToColor4())]
                : [.. parameter.ResolvedColors.Select(color => color.ToColor4())];
            local = GetLocalMatrix(itemTime);

            return geometry.Faces.Length > 0;
        }

        private SolidShape Shape()
            => new(parameter.Solid, parameter.Segments, parameter.Thickness, parameter.Form, parameter.Depth);

        private SurfaceGeometry Geometry(in FrameContext itemTime)
        {
            var shape = Shape();
            var outline = parameter.IsCustom ? parameter.OutlinePoints(itemTime) : [];

            var mesh = parameter.UsesEditedGeometry ? parameter.EditedGeometry(itemTime) : null;

            if (built is { } found
                && found.Shape == shape
                && ReferenceEquals(found.Mesh, mesh)
                && found.Outline.SequenceEqual(outline))
            {
                return found.Geometry;
            }

            var made = mesh ?? (shape.Kind == SolidKind.Custom
                ? OutlineSolid.Build(outline, shape.Form, shape.Depth / 100f, shape.Segments)
                : Solids.Get(shape.Kind, shape.Segments, shape.Thickness));

            built = new Built(shape, mesh, [.. outline], made);

            return made;
        }

        protected override void Prepare(TimelineItemSourceDescription description)
            => parameter.Targets = parameter.Modifiers.Any(modifier => modifier is BooleanModifier { IsEnabled: true }) ? BooleanTargets.Resolve(description, parameter) : null;

        private sealed record Built(SolidShape Shape, SurfaceGeometry? Mesh, Vector2[] Outline, SurfaceGeometry Geometry);

        private sealed class Frozen(Shape3DSource owner, SurfaceGeometry geometry, Matrix4x4 local) : I3DProvider, I3DBounds
        {
            public bool RequiresMappedTexture => false;

            public void Draw(in Render3DContext render, DrawContext3D item) => owner.Paint(render, item, local, geometry);

            public WorldBounds GetLocalBounds(in FrameContext itemTime) => WorldBounds.FromPoints(geometry.Vertices, local);
        }

        private Matrix4x4 GetLocalMatrix(in FrameContext itemTime) => parameter.GetLocalMatrix(itemTime);

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
            => WorldBounds.FromPoints(Geometry(itemTime).Vertices, GetLocalMatrix(itemTime));

        public override void Dispose()
        {
            painter.Dispose();
            resources.Dispose();
            base.Dispose();
        }

        private readonly record struct SolidShape(
            SolidKind Kind, int Segments, int Thickness, OutlineForm Form, int Depth);

        private sealed class SolidResources(ID3D11Device device) : IDisposable
        {
            private SurfaceMesh? mesh;
            private SurfaceGeometry? builtGeometry;
            private Color[] builtColors = [];

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            public SurfaceMesh GetMesh(SurfaceGeometry built, IReadOnlyList<Color> colors)
            {
                if (mesh is { } existing
                    && ReferenceEquals(builtGeometry, built)
                    && builtColors.SequenceEqual(colors))
                {
                    return existing;
                }

                mesh?.Dispose();
                builtGeometry = built;
                builtColors = [.. colors];

                return mesh = new SurfaceMesh(device, built, [.. colors.Select(color => color.ToColor4())]);
            }

            public void Dispose()
            {
                mesh?.Dispose();
                mesh = null;
                Pipeline.Dispose();
            }
        }
    }
}
