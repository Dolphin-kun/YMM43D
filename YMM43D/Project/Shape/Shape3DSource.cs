using System.Numerics;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using Color4 = Vortice.Mathematics.Color4;

namespace YMM43D.Project.Shape
{
    internal sealed class Shape3DSource(IGraphicsDevicesAndContext devices, Shape3DParameter parameter)
        : Shape3DSourceBase(devices)
    {
        private readonly Shape3DParameter parameter = parameter;
        private readonly DeviceResourceCache<SolidResources> resources = new(device => new SolidResources(device));

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var world = GetLocalMatrix(item.Time) * item.World;
            var constants = render.CreateConstants(world, item.Opacity, parameter.IsUnlit);

            var shared = resources.Get(render.Device);
            var mesh = shared.GetMesh(Shape(), parameter.ResolvedColors);

            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Front), mesh);
            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Back), mesh);
        }

        private SolidShape Shape() => new(parameter.Solid, parameter.Segments, parameter.Thickness);

        private float GetSize(in FrameContext itemTime)
            => WorldScale.ToWorld(parameter.Size.GetFloat(itemTime));

        private Matrix4x4 GetLocalMatrix(in FrameContext itemTime)
            => Matrix4x4.CreateScale(GetSize(itemTime))
             * Rotation3D.ForObject(
                   parameter.RotationX.GetFloat(itemTime),
                   parameter.RotationY.GetFloat(itemTime),
                   parameter.RotationZ.GetFloat(itemTime));

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            var shape = Shape();

            return WorldBounds.FromPoints(
                Solids.Get(shape.Kind, shape.Segments, shape.Thickness).Vertices,
                GetLocalMatrix(itemTime));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private readonly record struct SolidShape(SolidKind Kind, int Segments, int Thickness);

        private sealed class SolidResources(ID3D11Device device) : IDisposable
        {
            private SurfaceMesh? mesh;
            private SolidShape builtShape;
            private Color[] builtColors = [];

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device, Vertex.InputElements, new VertexColorMaterial(device));

            public SurfaceMesh GetMesh(in SolidShape shape, IReadOnlyList<Color> colors)
            {
                if (mesh is { } existing && builtShape == shape && builtColors.SequenceEqual(colors))
                    return existing;

                mesh?.Dispose();
                builtShape = shape;
                builtColors = [.. colors];

                return mesh = new SurfaceMesh(
                    device,
                    Solids.Get(shape.Kind, shape.Segments, shape.Thickness),
                    [.. colors.Select(ToColor4)]);
            }

            private static Color4 ToColor4(Color color)
                => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

            public void Dispose()
            {
                mesh?.Dispose();
                mesh = null;
                Pipeline.Dispose();
            }
        }
    }
}
