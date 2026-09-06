#include "PointCloud.hlsli"

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

float3 Shape(float3 cell)
{
    float3 ratio = Ratio(cell);

    // 画像は Y が下向き、3D 空間は上向き。
    float3 local = float3(
         (ratio.x - 0.5) * Extent.x,
        -(ratio.y - 0.5) * Extent.y,
         (ratio.z - 0.5) * Extent.z);

    return Deform(local);
}

float3 Place(float3 cell)
{
    // ばらつきは変形のあとに足す。先に足すと、散らばりまで一緒に
    // 曲げられて、量が場所によって変わってしまう。
    return Shape(cell) + Hash(cell) * Scatter;
}

// 面は隣の点との差から本物の法線を出す。ばらつきは入れない。入れると
// 点ごとに向きが飛んで、面がざらついて見える。
float3 SurfaceNormal(float3 cell)
{
    float3 along = Shape(cell + float3(1, 0, 0)) - Shape(cell - float3(1, 0, 0));
    float3 down = Shape(cell + float3(0, 1, 0)) - Shape(cell - float3(0, 1, 0));

    float3 normal = cross(along, down);

    return dot(normal, normal) > 1e-12 ? normalize(normal) : -ViewForward;
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

    output.Shading = 0.0;

    if (any(input.Other != input.Cell))
    {
        // 引かないと決まった線は、手前より奥へ送って刈り取らせる。
        // ピクセルシェーダーまで運んでから捨てるより安い。
        if (output.Random.y < LineRandomness)
        {
            output.Position = float4(0, 0, -1, 1);
            output.Nrm = float3(0, 0, 0);
            output.World = float3(0, 0, 0);
            return output;
        }

        // 線。相手へ向かう向きと視線から、画面に正対する幅の向きを作る。
        float3 along = Place(input.Other) - local;
        float3 side = cross(along, ViewForward);
        float length2 = dot(side, side);

        if (length2 > 1e-12)
            local += normalize(side) * input.Corner.x * LineHalfWidth;

        // 線はいつも円柱として塗る。粒の形は線には関わらない。
        output.Shading = 1.0;
    }
    else if (any(input.Corner != 0.0))
    {
        // 粒。カメラに正対させる。面では Corner が 0 なのでここへ来ない。
        local += (ViewRight * input.Corner.x + ViewUp * input.Corner.y) * PointHalfSize;

        output.Shading = PointIsRound > 0.5 ? 1.0 : 2.0;
    }

    output.Position = mul(float4(local, 1.0), WorldViewProjection);
    output.Nrm = mul(float4(SurfaceNormal(input.Cell), 0.0), WorldInverse).xyz;
    output.World = mul(float4(local, 1.0), World).xyz;
    return output;
}
