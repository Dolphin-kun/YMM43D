using System.Numerics;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Effects.Flat3D
{
    internal sealed class Flat3DProcessor(Flat3DEffect effect, IGraphicsDevicesAndContext devices) : VideoEffect3DProcessorBase(effect, devices)
    {
        private static readonly Vector3[] Corners =
        [
            new(-0.5f, -0.5f, 0f),
            new(0.5f, -0.5f, 0f),
            new(-0.5f, 0.5f, 0f),
            new(0.5f, 0.5f, 0f),
        ];

        private readonly Flat3DEffect effect = effect;
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines = new(
                device => new RenderPipeline<TransformConstants>(
                    device,
                    new PlaneMesh(device),
                    new TextureMaterial(device)));

        public override bool ScalesToInputSize => false;

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            var world = GetLocalMatrix(time) * item.World;

            var constants = render.CreateConstants(world, item.Opacity, effect.IsUnlit);

            var settings = item.ToDrawSettings(FaceCulling.None, texture) with
            {
                SkipDepthWrite = !effect.WritesDepth,
            };

            pipelines.Get(render.Device).Draw(render.Context, constants, settings);
        }

        private Matrix4x4 GetLocalMatrix(in FrameContext time)
        {
            var rotation = Rotation3D.ForObject(
                -effect.RotationX.GetFloat(time),
                -effect.RotationY.GetFloat(time),
                -effect.RotationZ.GetFloat(time));

            if (!TryGetSize(out var size, out var offset))
                return rotation;

            return WorldScale.CreateSizeMatrix(size, offset + size / 2f, rotation);
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
            => WorldBounds.FromPoints(Corners, GetLocalMatrix(itemTime));

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
