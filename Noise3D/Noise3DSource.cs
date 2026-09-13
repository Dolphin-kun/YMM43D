using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace Noise3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NoiseConstants
    {
        public Vector4 Color;

        public Vector4 Box;

        public Vector4 Camera;

        public Vector4 Offset;

        public Vector4 Feature;

        public Vector4 Tone;

        public Vector4 Fractal;

        public Vector4 Warp;
    }

    internal sealed class Noise3DSource(IGraphicsDevicesAndContext devices, Noise3DParameter parameter)
        : Shape3DSourceBase(devices)
    {
        private const float DensityPerPixel = 0.01f;

        private readonly Noise3DParameter parameter = parameter;
        private readonly DeviceResourceCache<NoiseResources> resources = new(device => new NoiseResources(device));

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (item.DepthOnly || !resources.TryGet(render.Device, out var shared))
                return;

            var time = item.Time;
            var box = parameter.GetBoxPixels(time);

            if (box.X <= 0f || box.Y <= 0f || box.Z <= 0f)
                return;

            var world = parameter.GetLocalMatrix(time) * item.World;

            if (!Matrix4x4.Invert(world, out var inverse))
                return;

            var camera = Vector3.Transform(render.GetCameraPosition(), inverse);
            var axis = NoiseSliceMesh.ChooseAxis(camera, box);

            var mesh = shared.GetMesh(parameter.Slices);
            mesh.Arrange(render.Context, axis, camera[axis]);

            shared.Pipeline.Draw(
                render.Context,
                render.CreateConstants(world, item.Opacity, unlit: true),
                CreateConstants(time, box, camera, axis),
                new DrawSettings
                {
                    Blend = BlendMode.Normal,
                    Culling = FaceCulling.None,
                    IgnoreDepth = item.IsAlwaysOnTop,
                    SkipDepthWrite = true,
                },
                mesh);
        }

        private NoiseConstants CreateConstants(in FrameContext time, Vector3 box, Vector3 camera, int axis)
        {
            var featureSize = parameter.GetFeatureSize(time);

            return new NoiseConstants
            {
                Color = new Vector4(
                    parameter.Color.ToVector3(),
                    parameter.Density.GetFloat(time) / 100f * DensityPerPixel),
                Box = new Vector4(box, Math.Clamp(parameter.EdgeBlur.GetFloat(time) / 100f, 0f, 1f)),
                Camera = new Vector4(camera, parameter.Slices),
                Offset = new Vector4(parameter.GetOffset(time), float.DegreesToRadians(parameter.Angle.GetFloat(time))),
                Feature = new Vector4(featureSize, parameter.Seed % 65536),
                Tone = new Vector4(
                    Math.Clamp(parameter.Strength.GetFloat(time) / 100f, 0f, 1f),
                    Math.Clamp(parameter.Threshold.GetFloat(time) / 100f, 0f, 1f),
                    MathF.Round(MathF.Max(parameter.Levels.GetFloat(time), 0f)),
                    (float)parameter.NoiseType),
                Fractal = new Vector4(
                    Math.Clamp(MathF.Round(parameter.Octaves.GetFloat(time)), 1f, 8f),
                    MathF.Max(parameter.Lacunarity.GetFloat(time), 1f),
                    Math.Clamp(parameter.Gain.GetFloat(time), 0f, 1f),
                    (float)parameter.FractalMode),
                Warp = new Vector4(
                    MathF.Max(parameter.WarpStrength.GetFloat(time), 0f),
                    MathF.Max(parameter.WarpScale.GetFloat(time), 1f) / 100f,
                    axis,
                    0f),
            };
        }

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            var corners = new Vector3[8];

            for (var i = 0; i < corners.Length; i++)
                corners[i] = new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f);

            return WorldBounds.FromPoints(corners, parameter.GetLocalMatrix(itemTime));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private sealed class NoiseResources(ID3D11Device device) : IDisposable
        {
            private NoiseSliceMesh? mesh;

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(
                device,
                NoiseVertex.InputElements,
                new ShaderMaterial(device, typeof(NoiseResources).Assembly, "Noise3D.hlsl"));

            public NoiseSliceMesh GetMesh(int slices)
            {
                if (mesh is { } existing && existing.SliceCount == slices)
                    return existing;

                mesh?.Dispose();

                return mesh = new NoiseSliceMesh(device, slices);
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
