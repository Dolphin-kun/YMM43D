using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace BeamLight3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BeamConstants
    {
        public Vector4 Color;

        public Vector4 Shape;
    }

    internal sealed class BeamLight3DSource(
        IGraphicsDevicesAndContext devices, BeamLight3DParameter parameter)
        : Shape3DSourceBase(devices)
    {
        private const int BoundsRimPoints = 12;

        private readonly BeamLight3DParameter parameter = parameter;
        private readonly DeviceResourceCache<BeamResources> resources = new(device => new BeamResources(device));

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (item.DepthOnly || !resources.TryGet(render.Device, out var shared))
                return;

            var world = parameter.GetLocalMatrix(item.Time) * item.World;
            var scene = render.CreateConstants(world, item.Opacity, unlit: true);

            var mesh = shared.GetMesh(BeamShape.Create(parameter.Detail, parameter.EdgeBlur));

            shared.Pipeline.Draw(render.Context, scene, Constants(item.Time), new DrawSettings
            {
                Blend = BlendMode.Accumulate,
                Culling = FaceCulling.None,
                IgnoreDepth = item.IsAlwaysOnTop,
                SkipDepthWrite = true,
            }, mesh);
        }

        private BeamConstants Constants(in FrameContext itemTime) => new()
        {
            Color = new Vector4(parameter.BeamColor.ToVector3(), parameter.Density.GetFloat(itemTime) / 100f),
            Shape = new Vector4(ToExponent(parameter.Decay.GetFloat(itemTime)), 0f, 0f, 0f),
        };

        private static float ToExponent(float percent)
            => MathF.Pow(2f, (Math.Clamp(percent, 0f, 100f) - 50f) / 25f);

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            var points = new Vector3[BoundsRimPoints + 1];

            points[0] = Vector3.Zero;

            for (var i = 0; i < BoundsRimPoints; i++)
            {
                var angle = MathF.Tau * i / BoundsRimPoints;

                points[i + 1] = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 1f);
            }

            return WorldBounds.FromPoints(points, parameter.GetLocalMatrix(itemTime));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private sealed class BeamResources(ID3D11Device device) : IDisposable
        {
            private BeamMesh? mesh;

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device,
                BeamVertex.InputElements,
                new ShaderMaterial(device, typeof(BeamResources).Assembly, "Beam.hlsl"));

            public BeamMesh GetMesh(in BeamShape shape)
            {
                if (mesh is { } existing && existing.Shape == shape)
                    return existing;

                mesh?.Dispose();

                return mesh = new BeamMesh(device, shape);
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
