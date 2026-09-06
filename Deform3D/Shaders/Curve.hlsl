#include "Deform.hlsli"

cbuffer CurveConstants : register(b1)
{
    float BendRadians;
    float TwistRadians;
    int   AlongY;
    int   CurvePadding;
};

float3 Deform(float3 local, float3 piece)
{
    // 縦に曲げるときは軸を入れ替えて、同じ計算を通す。
    float3 p = AlongY ? float3(local.y, local.x, local.z) : local;

    if (abs(BendRadians) > 1e-4)
    {
        // 板の幅 1 をぐるりと BendRadians だけ回した円弧に置き換える。
        float radius = 1.0 / BendRadians;
        float angle = p.x * BendRadians;

        p = float3(radius * sin(angle), p.y, p.z + radius * (1.0 - cos(angle)));
    }

    if (abs(TwistRadians) > 1e-4)
    {
        // 縦の位置に応じて、軸まわりにひねる。
        float angle = p.y * TwistRadians;
        float s = sin(angle);
        float c = cos(angle);

        p = float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
    }

    return AlongY ? float3(p.y, p.x, p.z) : p;
}

float DeformFade(float3 local, float3 piece)
{
    return 1.0;
}
