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

        /// <summary>カメラの右方向を、この形状のローカル座標系に持ち込んだもの。</summary>
        public Vector3 ViewRight;

        /// <summary>粒の一辺の半分（ワールド単位）。</summary>
        public float PointHalfSize;

        /// <summary>カメラの上方向。</summary>
        public Vector3 ViewUp;

        /// <summary>線の太さの半分（ワールド単位）。</summary>
        public float LineHalfWidth;

        /// <summary>カメラの前方向。線を画面に正対させるのに使う。</summary>
        public Vector3 ViewForward;

        /// <summary>0 以外なら、色の代わりに画像の色を使う。</summary>
        public float UseSourceColor;

        /// <summary>この描画だけに掛かる不透明度。面に指定した値を入れる。</summary>
        public float ExtraOpacity;

        /// <summary>面ごとに不透明度を散らす量（0〜1）。</summary>
        public float OpacityRandomness;

        private Vector2 padding;
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
                float3 ViewRight;
                float  PointHalfSize;
                float3 ViewUp;
                float  LineHalfWidth;
                float3 ViewForward;
                float  UseSourceColor;
                float  ExtraOpacity;
                float  OpacityRandomness;
                float2 Padding;
            };

            struct VS_INPUT
            {
                float3 Cell   : CELL;
                float2 Corner : CORNER;
                float3 Other  : OTHER;
            };

            struct PS_INPUT
            {
                float4 Position : SV_POSITION;
                float2 TexCoord : TEXCOORD;

                // 面ごとに一定の乱数。補間すると三角形の中で濃さが変わってしまう。
                nointerpolation float Random : RANDOM;
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

            // 格子番号を、格子の中での割合（0〜1）に直す。分割数が1なら中央。
            float3 Ratio(float3 cell)
            {
                return GridCount > 1.5 ? cell / max(GridCount - 1.0, 1.0) : 0.5;
            }

            // 格子番号から、ばらつきまで含めた点の位置を求める。
            float3 Place(float3 cell)
            {
                float3 ratio = Ratio(cell);

                // 画像は Y が下向き、3D 空間は上向き。
                float3 local = float3(
                     (ratio.x - 0.5) * Extent.x,
                    -(ratio.y - 0.5) * Extent.y,
                     (ratio.z - 0.5) * Extent.z);

                return local + Hash(cell) * Scatter;
            }

            PS_INPUT VSMain(VS_INPUT input)
            {
                PS_INPUT output;

                output.TexCoord = Ratio(input.Cell).xy;
                output.Random = Hash(input.Cell).x * 0.5 + 0.5;

                float3 local = Place(input.Cell);

                if (any(input.Other != input.Cell))
                {
                    // 線。相手へ向かう向きと視線から、画面に正対する幅の向きを作る。
                    float3 along = Place(input.Other) - local;
                    float3 side = cross(along, ViewForward);
                    float length2 = dot(side, side);

                    if (length2 > 1e-12)
                        local += normalize(side) * input.Corner.x * LineHalfWidth;
                }
                else
                {
                    // 粒。カメラに正対させる。面では Corner が 0 なので効かない。
                    local += (ViewRight * input.Corner.x + ViewUp * input.Corner.y) * PointHalfSize;
                }

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

                // ばらつきは 1 倍から Random 倍までの間で効かせる。
                float scatter = lerp(1.0, input.Random, OpacityRandomness);

                return float4(rgb, Color.a * Opacity * ExtraOpacity * scatter);
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
