using System.Numerics;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace Bezier3D
{
    internal sealed class Bezier3DSource(IGraphicsDevicesAndContext devices, Bezier3DParameter parameter) : Shape3DSourceBase(devices), I3DGeometrySource
    {
        private readonly Bezier3DParameter parameter = parameter;
        private readonly DeviceResourceCache<CurveResources> resources = new(device => new CurveResources(device));

        private Built? built;
        private Color[] colors = [];

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            var made = Geometry(item.Time);

            if (made.Faces.Length == 0)
                return;

            var world = parameter.GetLocalMatrix(item.Time) * item.World;
            var constants = render.CreateConstants(world, item.Opacity, parameter.IsUnlit, item.AlphaCutoff, parameter.GetGloss(item.Time));
            var mesh = shared.GetMesh(made, Colors());

            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Front), mesh);
            shared.Pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Back), mesh);
        }

        public bool TryGetGeometry(in FrameContext itemTime, out SurfaceGeometry geometry, out IReadOnlyList<Vortice.Mathematics.Color4> colors, out Matrix4x4 local)
        {
            geometry = Geometry(itemTime);
            colors = [.. Colors().Select(color => color.ToColor4())];
            local = parameter.GetLocalMatrix(itemTime);

            return geometry.Faces.Length > 0;
        }

        private Color[] Colors()
        {
            var from = parameter.FromColor;
            var to = parameter.ToColor;

            if (colors.Length == BezierTube.GroupSteps && colors[0] == from && colors[^1] == to)
                return colors;

            var made = new Color[BezierTube.GroupSteps];

            for (var i = 0; i < made.Length; i++)
            {
                var at = (float)i / (made.Length - 1);

                made[i] = Color.FromArgb(Mix(from.A, to.A, at), Mix(from.R, to.R, at), Mix(from.G, to.G, at), Mix(from.B, to.B, at));
            }

            return colors = made;
        }

        private static byte Mix(byte from, byte to, float at) => (byte)Math.Clamp(MathF.Round(from + (to - from) * at), 0f, 255f);

        private sealed record Built(Shaped Shape, SurfaceGeometry Geometry);

        private readonly record struct Shaped(
            Vector3[] Points,
            float Radius,
            float Taper,
            float LengthRate,
            float Offset,
            float[] Pattern,
            int Segments,
            int Sides,
            bool Closed,
            BezierLineKind Kind,
            BezierSection Section,
            BezierCap Start,
            BezierCap End,
            BezierCap Dash)
        {
            public bool Equals(Shaped other)
                => Points.AsSpan().SequenceEqual(other.Points)
                && Radius == other.Radius
                && Taper == other.Taper
                && LengthRate == other.LengthRate
                && Offset == other.Offset
                && Pattern.AsSpan().SequenceEqual(other.Pattern)
                && Segments == other.Segments
                && Sides == other.Sides
                && Closed == other.Closed
                && Kind == other.Kind
                && Section == other.Section
                && Start == other.Start
                && End == other.End
                && Dash == other.Dash;

            public override int GetHashCode() => HashCode.Combine(Points.Length, Radius, LengthRate, Segments, Sides);
        }

        private Shaped Describe(in FrameContext itemTime) => new(
            parameter.GetPoints(itemTime),
            parameter.GetRadius(itemTime),
            parameter.GetTaper(itemTime),
            parameter.GetLengthRate(itemTime),
            parameter.DashOffset.GetFloat(itemTime),
            parameter.GetDashPattern(),
            parameter.Segments,
            parameter.Sides,
            parameter.IsClosed,
            parameter.LineType,
            parameter.Section,
            parameter.StartCap,
            parameter.EndCap,
            parameter.DashCap);

        private SurfaceGeometry Geometry(in FrameContext itemTime)
        {
            var shape = Describe(itemTime);

            if (built is { } found && found.Shape.Equals(shape))
                return found.Geometry;

            var path = BezierPath.Trace(shape.Points, shape.Kind, shape.Closed, shape.Segments);
            var runs = BezierPath.Runs(path, shape.Pattern, MathF.Max(shape.Radius * 2f, 1e-4f), shape.Offset, shape.LengthRate);

            var made = BezierTube.Build(
                runs,
                shape.Radius,
                shape.Taper,
                shape.Sides,
                shape.Section == BezierSection.Flat,
                shape.Start,
                shape.End,
                shape.Dash);

            built = new Built(shape, made);

            return made;
        }

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            var made = Geometry(itemTime);

            return made.Vertices.Length == 0 ? WorldBounds.Empty : WorldBounds.FromPoints(made.Vertices, parameter.GetLocalMatrix(itemTime));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private sealed class CurveResources(ID3D11Device device) : IDisposable
        {
            private SurfaceMesh? mesh;
            private SurfaceGeometry? builtGeometry;
            private Color[] builtColors = [];

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(device, Vertex.InputElements, new VertexColorMaterial(device));

            public SurfaceMesh GetMesh(SurfaceGeometry made, IReadOnlyList<Color> shades)
            {
                if (mesh is { } existing && ReferenceEquals(builtGeometry, made) && builtColors.SequenceEqual(shades))
                    return existing;

                mesh?.Dispose();
                builtGeometry = made;
                builtColors = [.. shades];

                return mesh = new SurfaceMesh(device, made, [.. shades.Select(shade => shade.ToColor4())]);
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
