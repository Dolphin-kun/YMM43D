using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace LiquidGlass
{
    internal sealed class LiquidGlassCustomEffect(IGraphicsDevicesAndContext devices)
        : D2D1CustomShaderEffectBase(Create<EffectImpl>(devices))
    {
        public Vector4 Placement { set => SetValue((int)Property.Placement, value); }

        public Vector4 Form { set => SetValue((int)Property.Form, value); }

        public Vector4 Glass { set => SetValue((int)Property.Glass, value); }

        public Vector4 Tint { set => SetValue((int)Property.Tint, value); }

        public Vector4 Light { set => SetValue((int)Property.Light, value); }

        private enum Property
        {
            Placement,
            Form,
            Glass,
            Tint,
            Light,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Constants
        {
            public Vector4 Placement;
            public Vector4 Form;
            public Vector4 Glass;
            public Vector4 Tint;
            public Vector4 Light;
        }

        [CustomEffect(2, "LiquidGlass", "リキッドグラス", "YMM43D", "YMM43D")]
        private sealed class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
        {
            private static readonly Lazy<byte[]> shader = new(CompileShader);

            private Constants constants;

            public EffectImpl() : base(shader.Value)
            {
            }

            [CustomEffectProperty(PropertyType.Vector4, (int)Property.Placement)]
            public Vector4 Placement
            {
                get => constants.Placement;
                set { constants.Placement = value; UpdateConstants(); }
            }

            [CustomEffectProperty(PropertyType.Vector4, (int)Property.Form)]
            public Vector4 Form
            {
                get => constants.Form;
                set { constants.Form = value; UpdateConstants(); }
            }

            [CustomEffectProperty(PropertyType.Vector4, (int)Property.Glass)]
            public Vector4 Glass
            {
                get => constants.Glass;
                set { constants.Glass = value; UpdateConstants(); }
            }

            [CustomEffectProperty(PropertyType.Vector4, (int)Property.Tint)]
            public Vector4 Tint
            {
                get => constants.Tint;
                set { constants.Tint = value; UpdateConstants(); }
            }

            [CustomEffectProperty(PropertyType.Vector4, (int)Property.Light)]
            public Vector4 Light
            {
                get => constants.Light;
                set { constants.Light = value; UpdateConstants(); }
            }

            protected override void UpdateConstants()
                => drawInformation?.SetPixelShaderConstantBuffer(constants);

            public override int GetInputCount() => 2;

            public override void PrepareForRender(ChangeType changeType)
            {
                base.PrepareForRender(changeType);
                UpdateConstants();
            }

            private float SampleReach
                => MathF.Abs(constants.Glass.X) * (1f + constants.Glass.Y) + 2f;

            private RawRect GlassBounds()
            {
                var reach = MathF.Sqrt(constants.Placement.Z * constants.Placement.Z + constants.Placement.W * constants.Placement.W)
                          + constants.Light.W + 2f;

                return new RawRect(
                    (int)MathF.Floor(constants.Placement.X - reach),
                    (int)MathF.Floor(constants.Placement.Y - reach),
                    (int)MathF.Ceiling(constants.Placement.X + reach),
                    (int)MathF.Ceiling(constants.Placement.Y + reach));
            }

            public override void MapInputRectsToOutputRect(
                RawRect[] inputRects, RawRect[] inputOpaqueSubRects, out RawRect outputRect, out RawRect outputOpaqueSubRect)
            {
                var source = inputRects.Length > 0 ? ClampInputRect(inputRects[0]) : default;
                inputRect = source;

                outputRect = constants.Glass.W > 0.5f ? Union(source, GlassBounds()) : GlassBounds();
                outputOpaqueSubRect = default;
            }

            public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
            {
                var reach = (int)MathF.Ceiling(SampleReach);
                var expanded = new RawRect(
                    outputRect.Left - reach, outputRect.Top - reach, outputRect.Right + reach, outputRect.Bottom + reach);

                for (var i = 0; i < inputRects.Length; i++)
                    inputRects[i] = ClampInputRect(expanded);
            }

            public override RawRect MapInvalidRect(int inputIndex, RawRect invalidInputRect)
            {
                var reach = (int)MathF.Ceiling(SampleReach);

                return Union(
                    new RawRect(
                        invalidInputRect.Left - reach, invalidInputRect.Top - reach,
                        invalidInputRect.Right + reach, invalidInputRect.Bottom + reach),
                    GlassBounds());
            }

            private static RawRect Union(RawRect a, RawRect b)
            {
                if (a.Right <= a.Left || a.Bottom <= a.Top)
                    return b;

                return new RawRect(
                    Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top),
                    Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
            }

            private static byte[] CompileShader()
            {
                using var stream = typeof(EffectImpl).Assembly.GetManifestResourceStream("LiquidGlass.Shaders.LiquidGlass.hlsl")
                    ?? throw new InvalidOperationException("LiquidGlass.hlsl が見つかりません。");
                using var reader = new StreamReader(stream);

                return ShaderCompiler.Compile(reader.ReadToEnd(), "main", "ps_4_0", "LiquidGlass.hlsl");
            }
        }
    }
}
