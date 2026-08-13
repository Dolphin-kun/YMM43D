using System.Numerics;
using YMM43D.Graphics;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace Extrusion3D
{
    internal sealed class Extrusion3DProcessor(Extrusion3DEffect effect, IGraphicsDevicesAndContext devices) : VideoEffect3DProcessorBase(effect, devices)
    {
        private readonly Extrusion3DEffect effect = effect;
        private readonly DeviceResourceCache<RenderPipeline<ExtrusionConstants>> pipelines = new(
                device => new RenderPipeline<ExtrusionConstants>(
                    device,
                    BoxMesh.CreateExtrusionBox(device),
                    new ExtrusionMaterial(device)));

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            var thickness = GetThickness(time);
            if (thickness <= 0)
                return;

            var world = Matrix4x4.CreateScale(1f, 1f, thickness) * item.World;

            Matrix4x4.Invert(world, out var inverseWorld);
            var cameraLocalPos = Vector3.Transform(render.GetCameraPosition(), inverseWorld);

            var sideColor = effect.SideColor;
            var constants = new ExtrusionConstants
            {
                Transform = render.CreateConstants(world, item.Opacity, effect.IsUnlit),
                SideColor = new Vector4(sideColor.R, sideColor.G, sideColor.B, sideColor.A) / 255f,
                CameraLocalPos = cameraLocalPos,
                ExtrusionType = (int)effect.ExtrusionType,
                Attenuation = effect.Attenuation.GetFloat(time) / 100f,
            };

            var settings = item.ToDrawSettings(FaceCulling.Front, texture) with { Blend = BlendMode.Normal };
            pipelines.Get(render.Device).Draw(render.Context, constants, settings);
        }

        private float GetThickness(in FrameContext time)
            => WorldScale.ToWorld(effect.Thickness.GetFloat(time));

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            var thickness = GetThickness(itemTime);
            if (thickness <= 0)
                return WorldBounds.Empty;

            return new WorldBounds(
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, thickness));
        }

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
