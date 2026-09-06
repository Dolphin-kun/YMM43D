#include "Deform.hlsli"

static const int WaveAcross = 0;
static const int WaveDown = 1;
static const int WaveRipple = 2;

cbuffer WaveConstants : register(b1)
{
    float Amplitude;
    float Wavelength;
    float PhaseRadians;
    int   WaveMode;
};

float3 Deform(float3 local, float3 piece)
{
    // 波の進む向きに沿った距離。同心円のときは中心からの距離。
    float travelled =
        WaveMode == WaveAcross ? local.x :
        WaveMode == WaveDown   ? local.y :
                                 length(local.xy);

    float wave = sin(travelled * 6.2831853 / max(Wavelength, 1e-4) + PhaseRadians);

    return float3(local.x, local.y, local.z + wave * Amplitude);
}

float DeformFade(float3 local, float3 piece)
{
    return 1.0;
}
