#include "../../YMM43D/Shaders/Deform.hlsli"

cbuffer ShatterConstants : register(b1)
{
    float Seconds;          // 衝撃からの経過。負のあいだは元のまま
    float FlySpeed;
    float FallSpeed;
    float Delay;

    float3 ImpactPoint;     // どこから当たったか
    float Impact;

    float RandomRotate;
    float RandomVector;
    float SpinRate;
    float ShatterPadding;
};

// 破片ごとの、飛び始めてからの秒数。
float PieceSeconds(float3 piece, float3 noise)
{
    return max(Seconds - noise.z * Delay, 0.0);
}

float3 Deform(float3 local, float3 piece)
{
    if (Seconds <= 0.0)
        return local;

    float3 noise = PieceNoise(piece, 0.0);
    float t = PieceSeconds(piece, noise);

    if (t <= 0.0)
        return local;

    float3 away = piece - ImpactPoint;
    float reach = length(away);

    float3 heading = reach > 1e-4 ? away / reach : float3(0.0, 0.0, 1.0);
    heading = normalize(heading + (noise * 2.0 - 1.0) * RandomVector);

    // 当たったところに近い破片ほど強く弾かれる。
    float strength = FlySpeed * (1.0 + Impact * saturate(1.0 - reach));

    float3 moved = piece + heading * strength * t;
    moved.y -= FallSpeed * t * t;

    // 破片は中心のまわりで回る。ちぎれないよう、同じ破片の点は同じだけ回す。
    float3 axis = normalize(noise * 2.0 - 1.0 + float3(0.0, 0.0, 0.3));
    float3 offset = RotateAround(local - piece, axis, SpinRate * RandomRotate * t);

    return moved + offset;
}

float DeformFade(float3 local, float3 piece)
{
    return 1.0;
}
