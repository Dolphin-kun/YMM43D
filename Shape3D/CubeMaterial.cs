using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace Shape3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CubeConstants
    {
        public Matrix4x4 WorldViewProjection;
        public Vector4 Color;
        public float Opacity;
        public float UseTexture;
        private Vector2 padding;

        public static CubeConstants Create(
            in Matrix4x4 worldViewProjection,
            System.Windows.Media.Color color,
            float opacity,
            bool hasTexture) => new()
            {
                WorldViewProjection = Matrix4x4.Transpose(worldViewProjection),
                Color = new Vector4(color.R, color.G, color.B, color.A) / 255f,
                Opacity = opacity,
                UseTexture = hasTexture ? 1f : 0f,
            };
    }

    internal sealed class CubeMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        private const string Source = """
            cbuffer CubeConstants : register(b0)
            {
                matrix WorldViewProjection;
                float4 Color;
                float  Opacity;
                float  UseTexture;
                float2 Padding;
            };

            Texture2D    txDiffuse : register(t0);
            SamplerState samLinear : register(s0);

            struct VS_IN { float3 Pos : POSITION; float4 Col : COLOR; float2 Tex : TEXCOORD; };
            struct PS_IN { float4 Pos : SV_POSITION; float2 Tex : TEXCOORD; };

            PS_IN VSMain(VS_IN input)
            {
                PS_IN output;
                output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
                output.Tex = input.Tex;
                return output;
            }

            float4 PSMain(PS_IN input) : SV_TARGET
            {
                float4 color = Color;

                // 画像は色の上に掛ける。色を白にすれば画像そのまま、画像を
                // 指定しなければ色そのままになる。
                if (UseTexture > 0.5)
                    color *= txDiffuse.Sample(samLinear, input.Tex);

                color.a *= Opacity;

                return color;
            }
            """;

        public CubeMaterial(ID3D11Device device)
        {
            VertexShaderBytecode = ShaderCompiler.Compile(Source, "VSMain", "vs_5_0", nameof(CubeMaterial));
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            var pixelShaderBytecode = ShaderCompiler.Compile(Source, "PSMain", "ps_5_0", nameof(CubeMaterial));
            PixelShader = device.CreatePixelShader(pixelShaderBytecode);
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
