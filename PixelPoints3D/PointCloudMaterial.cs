using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace PixelPoints3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointCloudConstants
    {
        public Matrix4x4 WorldViewProjection;

        public Vector4 Color;

        public Vector3 GridCount;

        public float Threshold;

        public Vector3 Extent;

        public float Opacity;

        public Vector3 Scatter;

        public float Seed;

        public Vector3 ViewRight;

        public float PointHalfSize;

        public Vector3 ViewUp;

        public float LineHalfWidth;

        public Vector3 ViewForward;

        public float UseSourceColor;

        public float ExtraOpacity;

        public float OpacityRandomness;

        public float PointIsRound;

        private readonly float padding;

        public Vector3 DeformAxis;

        public float DeformKind;

        public float DeformAmount;

        public float DeformPeriod;

        public float DeformPhase;

        public float LineRandomness;
    }

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
                float  PointIsRound;
                float  Padding;
                float3 DeformAxis;
                float  DeformKind;
                float  DeformAmount;
                float  DeformPeriod;
                float  DeformPhase;
                float  LineRandomness;
            };

            static const float Pi = 3.14159265;

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

                // 形の中心で 0、縁で ±1。Coverage() が縁を滑らかにするのに使う。
                float2 Edge : EDGE;

                // x … 格子の点ごと（面の不透明度）、y … 線ごと（引くかどうか）。
                nointerpolation float2 Random : RANDOM;
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

            // 点の並びを三次元的に歪ませる。種類ごとの「強さ」の換算は PointDeform が
            // 済ませてあるので、ここではワールド単位・ラジアン・比率をそのまま使う。
            float3 Deform(float3 p)
            {
                if (DeformKind < 0.5)
                    return p;

                float along = dot(p, DeformAxis);
                float halfSpan = max(dot(abs(DeformAxis), Extent) * 0.5, 1e-4);
                float3 across = p - DeformAxis * along;

                if (DeformKind < 1.5)
                {
                    // 波。軸に沿って進み、奥行き方向に押し引きする。
                    // 軸に奥行きを選んだときだけ、代わりに横方向へ押し引きする。
                    float3 side = abs(DeformAxis.z) > 0.5 ? float3(1, 0, 0) : float3(0, 0, 1);

                    return p + side * (DeformAmount * sin(2.0 * Pi * along / DeformPeriod + DeformPhase));
                }

                if (DeformKind < 2.5)
                {
                    // ねじれ。軸に沿って進むほど、軸のまわりに大きく回す。
                    float angle = DeformAmount * (along / halfSpan) + DeformPhase;

                    return DeformAxis * along
                         + across * cos(angle)
                         + cross(DeformAxis, across) * sin(angle);
                }

                if (DeformKind < 3.5)
                {
                    // 膨らみ。軸から遠いほど控えめに、中心ほど大きく軸の向きへ持ち上げる。
                    float limit = max(length(Extent - abs(DeformAxis) * Extent) * 0.5, 1e-4);
                    float ratio = saturate(length(across) / limit);

                    return p + DeformAxis * (DeformAmount * (1.0 - ratio * ratio));
                }

                // 球に巻く。軸に沿った位置が緯度、軸に垂直な1方向が経度、残る1方向が
                // 半径のずれになる。奥行きを持たせた格子は、入れ子の球殻として並ぶ。
                float3 east = abs(DeformAxis.x) > 0.5 ? float3(0, 0, 1) : float3(1, 0, 0);
                float3 up = cross(DeformAxis, east);

                float halfEast = max(dot(abs(east), Extent) * 0.5, 1e-4);

                float longitude = clamp(dot(p, east) / halfEast, -1.0, 1.0) * Pi + DeformPhase;
                float latitude = clamp(along / halfSpan, -1.0, 1.0) * (Pi * 0.5);
                float radius = halfEast + dot(p, up);

                float3 sphere = DeformAxis * (radius * sin(latitude))
                              + (east * cos(longitude) + up * sin(longitude)) * (radius * cos(latitude));

                return lerp(p, sphere, DeformAmount);
            }

            float3 Place(float3 cell)
            {
                float3 ratio = Ratio(cell);

                // 画像は Y が下向き、3D 空間は上向き。
                float3 local = float3(
                     (ratio.x - 0.5) * Extent.x,
                    -(ratio.y - 0.5) * Extent.y,
                     (ratio.z - 0.5) * Extent.z);

                // ばらつきは変形のあとに足す。先に足すと、散らばりまで一緒に
                // 曲げられて、量が場所によって変わってしまう。
                return Deform(local) + Hash(cell) * Scatter;
            }

            PS_INPUT VSMain(VS_INPUT input)
            {
                PS_INPUT output;

                output.TexCoord = Ratio(input.Cell).xy;

                // 線ごとの乱数は「両端を入れ替えても同じ値になる」式でなければならない。
                // 1本の線を作る四角形は from 側と to 側で Cell と Other が入れ替わって
                // いるため、偏った式にすると片側だけが刈り取られ、引き伸ばされた半端な
                // 三角形が線の無いところに残る。
                //
                // 和なら格子の中のどの線とも値がぶつからない。Cell と Other は隣り合う
                // 格子番号なので、和は「2×格子番号＋向き」の形になり、向きの成分は
                // 0 か 1、格子番号の成分は必ず偶数だから区別がつく。
                output.Random = float2(
                    Hash(input.Cell).x,
                    Hash(input.Cell + input.Other).y) * 0.5 + 0.5;

                // 粒は縦横、線は幅方向だけが ±1 に開く。面はどちらも 0 のまま。
                output.Edge = input.Corner;

                float3 local = Place(input.Cell);

                if (any(input.Other != input.Cell))
                {
                    // 引かないと決まった線は、手前より奥へ送って刈り取らせる。
                    // ピクセルシェーダーまで運んでから捨てるより安い。
                    if (output.Random.y < LineRandomness)
                    {
                        output.Position = float4(0, 0, -1, 1);
                        return output;
                    }

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

            // 形の縁がどれだけこの画素を覆っているかを返す。描画先はマルチサンプル
            // ではないので、これを掛けないと粒や線の縁が階段状になる。
            //
            // 1画素より細い線は、太さを保ったままギザギザに描かれるのではなく、
            // 薄くなって消えていく。面は edge が動かないので常に 1 が返り、
            // 隣り合う三角形の継ぎ目に隙間ができない。
            float Coverage(float2 edge)
            {
                if (PointIsRound > 0.5)
                {
                    float radius = length(edge);
                    return saturate((1.0 - radius) / max(fwidth(radius), 1e-6));
                }

                float2 width = fwidth(edge);
                float2 coverage = saturate((1.0 - abs(edge)) / max(width, 1e-6));

                return min(coverage.x, coverage.y);
            }

            float4 main(PS_INPUT input) : SV_Target
            {
                // 捨てたあとの画素は隣との差が定まらないので、割合は捨てる前に求める。
                float coverage = Coverage(input.Edge);

                float4 source = txDiffuse.SampleLevel(samLinear, input.TexCoord, 0);

                if (source.a < Threshold)
                    discard;

                float scatter = lerp(1.0, input.Random.x, OpacityRandomness);
                float alpha = Color.a * Opacity * ExtraOpacity * scatter * coverage;

                // 透けている画素も、捨てなければ深度は書いてしまう。円い粒では
                // 四角い板の隅がそのまま残り、後ろの粒を隠して黒く抜けて見える。
                if (alpha < 1.0 / 255.0)
                    discard;

                float3 rgb = UseSourceColor > 0.5 ? source.rgb : Color.rgb;

                return float4(rgb, alpha);
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
