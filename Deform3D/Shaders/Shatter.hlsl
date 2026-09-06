#include "Deform.hlsli"

cbuffer ShatterConstants : register(b1)
{
    float Progress;
    float FlightDistance;
    float Spin;
    float Gravity;

    float Seed;
    float Stagger;
    int   ShatterPadding0;
    int   ShatterPadding1;
};

// 破片が飛び始める時刻をずらしたうえで、0〜1 の進み具合に直す。
float PieceProgress(float3 piece)
{
    float delay = PieceNoise(piece, Seed).z * Stagger;

    return saturate((Progress - delay) / max(1.0 - delay, 1e-3));
}

float3 Deform(float3 local, float3 piece)
{
    if (Progress <= 0.0)
        return local;

    float t = PieceProgress(piece);
    if (t <= 0.0)
        return local;

    float3 noise = PieceNoise(piece, Seed);

    // 破片の中でどこにいるか。回すのはこの分だけで、中心はそのまま飛ばす。
    float3 offset = RotateAround(
        local - piece,
        normalize(noise * 2.0 - 1.0 + float3(0.0, 0.0, 0.3)),
        Spin * t);

    // 中心から外へ、少しばらつかせながら散らす。
    float2 outward = piece.xy;
    float reach = max(length(outward), 1e-4);

    float3 flight = float3(
        outward / reach * FlightDistance * (0.5 + noise.x),
        FlightDistance * (noise.y - 0.5));

    float3 moved = piece + flight * t;
    moved.y -= Gravity * t * t;

    return moved + offset;
}

float DeformFade(float3 local, float3 piece)
{
    if (Progress <= 0.0)
        return 1.0;

    float t = PieceProgress(piece);

    return 1.0 - t * t;
}
