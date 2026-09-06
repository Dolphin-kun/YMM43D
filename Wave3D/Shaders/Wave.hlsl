#include "../../YMM43D/Shaders/Deform.hlsli"

cbuffer WaveConstants : register(b1)
{
    float Amplitude;
    float Wavelength;
    float PhaseRadians;
    float AxisRadians;

    int   Ripple;
    int   WavePadding0;
    int   WavePadding1;
    int   WavePadding2;
};

float3 Deform(float3 local, float3 piece)
{
    // 波の進む向きに沿った距離。波紋のときは中心からの距離。
    float travelled = Ripple
        ? length(local.xy)
        : local.x * cos(AxisRadians) + local.y * sin(AxisRadians);

    float wave = sin(travelled * 6.2831853 / max(Wavelength, 1e-4) + PhaseRadians);

    return float3(local.x, local.y, local.z + wave * Amplitude);
}

float DeformFade(float3 local, float3 piece)
{
    return 1.0;
}
