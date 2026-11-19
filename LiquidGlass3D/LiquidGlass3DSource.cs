using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace LiquidGlass3D
{
    internal sealed class LiquidGlass3DSource(IGraphicsDevicesAndContext devices, LiquidGlass3DParameter parameter) : Shape3DSourceBase(devices)
    {
        private const float BoundsMargin = 1.6f;
        private const float NormalStep = 0.5f;

        private readonly LiquidGlass3DParameter parameter = parameter;
        private readonly DeviceResourceCache<GlassResources> resources = new(device => new GlassResources(device));

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (render.IsShadowPass || render.IsCapturingScene || !resources.TryGet(render.Device, out var shared))
                return;

            var time = item.Time;
            var size = parameter.GetSizePixels(time);

            if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
                return;

            var world = parameter.GetLocalMatrix(time) * item.World;

            if (!Matrix4x4.Invert(world, out var inverse))
                return;

            var front = SceneCapture.NearDepth(GetWorldBounds(time), item.World, render.View);
            var captured = item.DepthOnly ? null : shared.Capture.Capture(render, [this], front);
            var look = GlassLook.From(parameter, time);
            var constants = look.ToConstants(size, MathF.Max(parameter.CornerRadius.GetFloat(time), 0f), Vector3.Transform(render.GetCameraPosition(), inverse), (float)parameter.Shape, captured, NormalStep);
            var scene = render.CreateConstants(world, item.Opacity, alphaCutoff: 0f, gloss: look.Gloss);
            var settings = item.ToDrawSettings(FaceCulling.Front, captured?.View) with { Sampler = RenderStates.For(render.Device).LinearSampler };

            shared.Pipeline.Draw(render.Context, scene, constants, settings, shared.Mesh);
        }

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            var corners = new Vector3[8];
            var half = BoundsMargin * 0.5f;

            for (var i = 0; i < corners.Length; i++)
                corners[i] = new Vector3((i & 1) == 0 ? -half : half, (i & 2) == 0 ? -half : half, (i & 4) == 0 ? -half : half);

            return WorldBounds.FromPoints(corners, parameter.GetLocalMatrix(itemTime));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private sealed class GlassResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; } = new(device, Vertex.InputElements, new ShaderMaterial(device, typeof(GlassResources).Assembly, "LiquidGlass3D.hlsl"));

            public BoxMesh Mesh { get; } = BoxMesh.CreateUnitCube(device);

            public SceneCapture Capture { get; } = new(device);

            public void Dispose()
            {
                Capture.Dispose();
                Mesh.Dispose();
                Pipeline.Dispose();
            }
        }
    }
}
