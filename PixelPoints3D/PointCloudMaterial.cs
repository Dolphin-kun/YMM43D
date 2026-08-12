using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace PixelPoints3D
{
    /// <summary>
    /// 点群の定数バッファ。
    /// </summary>
    /// <remarks>
    /// レイアウトは <see cref="PointCloudMaterial"/> の HLSL 側 <c>cbuffer</c> 宣言と
    /// 1対1で対応します。片方だけを変更すると描画結果が壊れます。
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointCloudConstants
    {
        public Matrix4x4 WorldViewProjection;

        public Vector4 Color;

        /// <summary>格子の分割数（点の個数）。</summary>
        public Vector3 GridCount;

        /// <summary>この不透明度に満たない場所には点を打たない。</summary>
        public float Threshold;

        /// <summary>格子が占める大きさ（ワールド単位）。</summary>
        public Vector3 Extent;

        public float Opacity;

        /// <summary>ばらつきの最大量（ワールド単位）。</summary>
        public Vector3 Scatter;

        public float Seed;

        /// <summary>粒の横方向。カメラに正対させるため、ローカル座標系に持ち込んだもの。</summary>
        public Vector3 PointRight;

        /// <summary>0 以外なら、色の代わりに画像の色を使う。</summary>
        public float UseSourceColor;

        /// <summary>粒の縦方向。</summary>
        public Vector3 PointUp;

        private float padding;
    }

    /// <summary>
    /// 格子番号から点の位置を組み立て、画像に中身がある場所だけを残すシェーダー。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 頂点は座標を持たず、格子の何番目かだけを持ちます。位置・ばらつき・奥行きは
    /// すべてここで計算するので、パラメータを動かしてもバッファの作り直しが要りません。
    /// </para>
    /// <para>
    /// 中身があるかどうかの判定はピクセルシェーダーで行います。頂点側で捨てようとすると、
    /// 三角形や線の一部だけが消えて中途半端な形が残ります。
    /// </para>
    /// </remarks>
    internal sealed class PointCloudMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        private const string SharedDeclarations = """
            cbuffer PointCloudConstants : register(b0)
            {
                matrix WorldViewProjection;
                float4 Color;
                float3 GridCount;
                float  Threshold;
                float3 Extent;
                float  Opacity;
                float3 Scatter;
                float  Seed;
                float3 PointRight;
                float  UseSourceColor;
                float3 PointUp;
                float  Padding;
            };

            struct VS_INPUT
            {
                float3 Cell   : CELL;
                float2 Corner : CORNER;
            };

            struct PS_INPUT
            {
                float4 Position : SV_POSITION;
                float2 TexCoord : TEXCOORD;
            };
            """;

        private const string VertexShaderSource = """
            // 格子番号から、繰り返しの無い乱数を3つ作る。
            float3 Hash(float3 cell)
            {
                float3 p = cell + Seed * 17.13;
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx) * 2.0 - 1.0;
            }

            PS_INPUT VSMain(VS_INPUT input)
            {
                PS_INPUT output;

                // 端の点が格子の両端にちょうど乗るようにする。分割数が1なら中央。
                float3 steps = max(GridCount - 1.0, 1.0);
                float3 ratio = GridCount > 1.5 ? input.Cell / steps : 0.5;

                // 画像は Y が下向き、3D 空間は上向き。
                output.TexCoord = ratio.xy;

                float3 local = float3(
                     (ratio.x - 0.5) * Extent.x,
                    -(ratio.y - 0.5) * Extent.y,
                     (ratio.z - 0.5) * Extent.z);

                local += Hash(input.Cell) * Scatter;

                // 粒はカメラに正対させる。線と面では Corner が 0 なので効かない。
                local += PointRight * input.Corner.x + PointUp * input.Corner.y;

                output.Position = mul(float4(local, 1.0), WorldViewProjection);
                return output;
            }
            """;

        private const string PixelShaderSource = """
            Texture2D    txDiffuse : register(t0);
            SamplerState samLinear : register(s0);

            float4 main(PS_INPUT input) : SV_Target
            {
                float4 source = txDiffuse.SampleLevel(samLinear, input.TexCoord, 0);

                // 中身が無いところは描かない。
                if (source.a < Threshold)
                    discard;

                float3 rgb = UseSourceColor > 0.5 ? source.rgb : Color.rgb;

                return float4(rgb, Color.a * Opacity);
            }
            """;

        public PointCloudMaterial(ID3D11Device device)
        {
            VertexShaderBytecode = ShaderCompiler.Compile(
                SharedDeclarations + VertexShaderSource, "VSMain", "vs_5_0", nameof(PointCloudMaterial));
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            var pixelShaderBytecode = ShaderCompiler.Compile(
                SharedDeclarations + PixelShaderSource, "main", "ps_5_0", nameof(PointCloudMaterial));
            PixelShader = device.CreatePixelShader(pixelShaderBytecode);
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
