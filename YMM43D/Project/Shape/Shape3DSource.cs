using System.Numerics;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape
{
    internal sealed class Shape3DSource(IGraphicsDevicesAndContext devices, Shape3DParameter parameter)
        : Shape3DSourceBase(devices)
    {
        private readonly Shape3DParameter parameter = parameter;
        private readonly DeviceResourceCache<SolidResources> resources = new(device => new SolidResources(device));

        private SurfaceGeometry? geometry;
        private SurfaceGeometry? builtMesh;
        private SolidShape builtShape;
        private Vector2[] builtOutline = [];

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            var world = GetLocalMatrix(item.Time) * item.World;
            var constants = render.CreateConstants(world, item.Opacity, parameter.IsUnlit, item.AlphaCutoff, parameter.GetGloss(item.Time));

            var mesh = shared.GetMesh(Geometry(item.Time), parameter.ResolvedColors);

            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Front), mesh);
            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Back), mesh);
        }

        private SolidShape Shape()
            => new(parameter.Solid, parameter.Segments, parameter.Thickness, parameter.Form, parameter.Depth);

        private SurfaceGeometry Geometry(in FrameContext itemTime)
        {
            var shape = Shape();
            var outline = parameter.IsCustom ? parameter.OutlinePoints(itemTime) : [];

            if (geometry is { } found
                && builtShape == shape
                && ReferenceEquals(builtMesh, parameter.HasMesh ? parameter.EditedGeometry() : null)
                && builtOutline.SequenceEqual(outline))
            {
                return found;
            }

            builtMesh = parameter.HasMesh ? parameter.EditedGeometry() : null;

            builtShape = shape;
            builtOutline = [.. outline];

            if (parameter.HasMesh)
                return geometry = parameter.EditedGeometry();

            return geometry = shape.Kind == SolidKind.Custom
                ? OutlineSolid.Build(outline, shape.Form, shape.Depth / 100f, shape.Segments)
                : Solids.Get(shape.Kind, shape.Segments, shape.Thickness);
        }

        private Matrix4x4 GetLocalMatrix(in FrameContext itemTime) => parameter.GetLocalMatrix(itemTime);

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
            => WorldBounds.FromPoints(Geometry(itemTime).Vertices, GetLocalMatrix(itemTime));

        public override void Dispose()
        {
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
