using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace LiquidGlass
{
    internal sealed class LiquidGlassProcessor(IGraphicsDevicesAndContext devices, LiquidGlassEffect item)
        : VideoEffectProcessorBase(devices)
    {
        private readonly LiquidGlassEffect item = item;

        private GaussianBlur? blur;
        private LiquidGlassCustomEffect? glass;

        protected override ID2D1Image CreateEffect(IGraphicsDevicesAndContext devices)
        {
            blur = new GaussianBlur(devices.DeviceContext)
            {
                BorderMode = BorderMode.Soft,
                Optimization = GaussianBlurOptimization.Balanced,
            };
            disposer.Collect(blur);

            glass = new LiquidGlassCustomEffect(devices);
            disposer.Collect(glass);

            using (var blurred = blur.Output)
                glass.SetInput(1, blurred, true);

            var output = glass.Output;
            disposer.Collect(output);

            return output;
        }

        protected override void setInput(ID2D1Image? input)
        {
            blur?.SetInput(0, input, true);
            glass?.SetInput(0, input, true);
        }

        protected override void ClearEffectChain()
        {
            glass?.SetInput(0, null, true);
            glass?.SetInput(1, null, true);
            blur?.SetInput(0, null, true);
        }

        public override DrawDescription Update(EffectDescription effectDescription)
        {
            if (blur is null || glass is null)
                return effectDescription.DrawDescription;

            var frame = effectDescription.ItemPosition.Frame;
            var length = effectDescription.ItemDuration.Frame;
            var fps = effectDescription.FPS;

            float Value(Animation animation) => (float)animation.GetValue(frame, length, fps);

            var width = MathF.Max(Value(item.Width), 0f);
            var height = MathF.Max(Value(item.Height), 0f);
            var blurPixels = MathF.Max(Value(item.Blur), 0f);

            blur.StandardDeviation = blurPixels / 2f;

            glass.Placement = new Vector4(Value(item.X), Value(item.Y), width / 2f, height / 2f);
            glass.Form = new Vector4(
                MathF.Max(Value(item.CornerRadius), 0f),
                float.DegreesToRadians(Value(item.Rotation)),
                item.Shape == GlassShape.Ellipse ? 1f : 0f,
                MathF.Max(Value(item.Bevel), 0.001f));
            glass.Glass = new Vector4(
                Value(item.Refraction),
                MathF.Max(Value(item.Dispersion), 0f) / 100f,
                MathF.Max(Value(item.Brightness), 0f) / 100f,
                item.ShowsOutside ? 1f : 0f);
            glass.Tint = new Vector4(
                item.TintColor.ToVector3(),
                Math.Clamp(Value(item.TintAmount) / 100f, 0f, 1f));
            glass.Light = new Vector4(
                MathF.Max(Value(item.Highlight), 0f) / 100f,
                float.DegreesToRadians(Value(item.LightAngle)),
                Math.Clamp(Value(item.Shadow) / 100f, 0f, 1f),
                MathF.Max(Value(item.ShadowSpread), 0f));

            return effectDescription.DrawDescription;
        }
    }
}
