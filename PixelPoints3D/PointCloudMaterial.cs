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

        /// <summary>0 以外なら、粒を四角形ではなく円で描く。</summary>
        public float PointIsRound;

        private float padding;

        /// <summary>変形の軸（単位ベクトル）。</summary>
        public Vector3 DeformAxis;

        /// <summary>変形の種類。<see cref="PixelPoints3D.DeformKind"/> の値をそのまま入れる。</summary>
        public float DeformKind;

        /// <summary>変形の強さ。種類ごとに単位が違う。<see cref="PointDeform"/> が換算済み。</summary>
        public float DeformAmount;

        /// <summary>波1つ分の長さ（ワールド単位）。</summary>
        public float DeformPeriod;

        /// <summary>波やねじれのずれ（ラジアン）。</summary>
        public float DeformPhase;

        /// <summary>線を引かない割合（0〜1）。線を描くときだけ入れる。</summary>
        public float LineRandomness;
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

                // 形の中での位置。縁を滑らかにするのに使う。詳しくは Coverage() を参照。
                float2 Edge : EDGE;

                // 形ごとに一定の乱数。補間すると1つの形の中で値が変わってしまう。
                //   x … 格子の点ごと。面の不透明度に使う
                //   y … 線ごと（両端の組で決まる）。引くかどうかに使う
                //
                // 四角形は2枚の三角形とも先頭の頂点が同じなので、どちらの三角形でも
                // 同じ値になる。
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

            // 点の並びを三次元的に歪ませる。
            //
            // 種類ごとの「強さ」の換算は PointDeform が済ませてあるので、ここでは
            // ワールド単位・ラジアン・比率をそのまま使う。
            //
            // 線や面もこの結果を通るので、曲げても点と辺の対応は崩れない。
            // 端点それぞれを歪ませてから結ぶためで、辺は折れ線として素直に追従する。
            float3 Deform(float3 p)
            {
                if (DeformKind < 0.5)
                    return p;

                // 選んだ軸に沿った位置と、その向きの格子の半分の長さ。
                float along = dot(p, DeformAxis);
                float halfSpan = max(dot(abs(DeformAxis), Extent) * 0.5, 1e-4);

                // 軸に垂直な成分。ねじれと膨らみで使う。
                float3 across = p - DeformAxis * along;

                if (DeformKind < 1.5)
                {
                    // 波。選んだ軸に沿って進み、奥行き方向に押し引きする。
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

                // 球に巻く。選んだ軸を極にして、平らな並びを球の面へ移す。
                // 軸に沿った位置が緯度、軸に垂直な1方向が経度、残る1方向が半径のずれ
                // になる。奥行きを持たせた格子は、入れ子の球殻として並ぶ。
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

            // 格子番号から、ばらつきまで含めた点の位置を求める。
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

                // 線ごとの乱数は両端の組から作る。片方の点だけで決めると、その点から
                // 伸びる線が全部まとめて消えてしまい、まばらにならない。
                output.Random = float2(
                    Hash(input.Cell).x,
                    Hash(input.Cell + input.Other * 3.7 + 1.3).y) * 0.5 + 0.5;

                // 粒は縦横、線は幅方向だけが ±1 に開く。面はどちらも 0 のまま。
                output.Edge = input.Corner;

                float3 local = Place(input.Cell);

                if (any(input.Other != input.Cell))
                {
                    // 引かないと決まった線は、ここで捨てる。ピクセルシェーダーまで
                    // 運んでから捨てるより安い。手前より奥へ送ると刈り取られる。
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

            // 形の縁がどれだけこの画素を覆っているかを返す。
            //
            // 描画先はマルチサンプルではないので、粒や線の縁がそのままだと階段状に
            // なります。edge は形の中心で 0、縁で ±1 になる量なので、隣の画素との
            // 差（fwidth）で割れば「縁まであと何画素か」が分かります。これを
            // 0〜1 に丸めたものが覆っている割合です。
            //
            // 線が1画素より細くなると差のほうが大きくなり、割合は 1 に届きません。
            // 太さを保ったままギザギザに描くのではなく、薄く描いて消えていきます。
            //
            // 面は edge が 0 のまま動かないので、差も 0 になり、割合は常に 1 です。
            // 隣り合う三角形の継ぎ目に隙間を作らないための性質で、意図的です。
            //
            // 円い粒は、四角形の板の中で中心からの距離を見て縁を丸く抜きます。
            // 頂点の数は四角形と変わりません。
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
                // 覆っている割合は捨てる前に求める。捨てたあとの画素は隣との差が
                // 定まらなくなるため、後に回すと縁の計算が崩れる。
                float coverage = Coverage(input.Edge);

                float4 source = txDiffuse.SampleLevel(samLinear, input.TexCoord, 0);

                // 中身が無いところは描かない。
                if (source.a < Threshold)
                    discard;

                float3 rgb = UseSourceColor > 0.5 ? source.rgb : Color.rgb;

                // ばらつきは 1 倍から Random.x 倍までの間で効かせる。
                float scatter = lerp(1.0, input.Random.x, OpacityRandomness);

                return float4(rgb, Color.a * Opacity * ExtraOpacity * scatter * coverage);
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
