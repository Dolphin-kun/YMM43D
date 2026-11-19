#include "Texture.hlsli"

cbuffer SceneConstants : register(b0)
{
    matrix WorldViewProjection;
    matrix World;
    matrix WorldInverse;
    float4 CameraPosition;
    float4 Ambient;
    float4 FogColor;
    float4 Options;
    float4 Surface;
    float4 Shading;
    float4 Ground;
};

cbuffer ReflectionConstants : register(b1)
{
    matrix NormalMatrix;
    matrix PlaneInverse[3];
    matrix FaceViewProjection[6];
    matrix FaceInverse[6];
    float4 ViewportRect;
    float4 LookSettings;
    float4 ReflectOptions;
    float4 RippleSettings;
    float4 TintColor;
    float4 CubeCenter;
    float4 PlanarScale;
    float4 Slots[16];
};

#define Opacity       Options.x
#define ViewportStart ViewportRect.xy
#define ViewportSize  ViewportRect.zw
#define Strength      LookSettings.x
#define FresnelRate   LookSettings.y
#define PlanarMip     LookSettings.z
#define CubeMip       LookSettings.w
#define ReachDistance ReflectOptions.x
#define HasMask       ReflectOptions.y
#define IsTwoSided    ReflectOptions.z
#define HasCube       ReflectOptions.w
#define BackFacing    -0.2
#define RippleAmount  RippleSettings.x
#define RippleScale   RippleSettings.y
#define RipplePhase   RippleSettings.z
#define CubeSlot      3.0

Texture2D MaskTexture : register(t0);
Texture2DArray PlanarColor : register(t12);
Texture2DArray<float> PlanarDepth : register(t13);
Texture2DArray CubeColor : register(t14);
Texture2DArray<float> CubeDepth : register(t15);
SamplerState LinearSampler : register(s0);
SamplerState PointSampler : register(s2);

struct VS_IN
{
    float3 Pos : POSITION;
    float3 Nrm : NORMAL;
    float Plane : TEXCOORD0;
};

struct PS_IN
{
    float4 Pos : SV_POSITION;
    float3 World : TEXCOORD0;
    float3 Nrm : NORMAL;
    float3 Local : TEXCOORD1;
    nointerpolation float Plane : TEXCOORD2;
};

PS_IN VSMain(VS_IN input)
{
    PS_IN output;

    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
    output.World = mul(float4(input.Pos, 1.0), World).xyz;
    output.Nrm = mul(float4(input.Nrm, 0.0), NormalMatrix).xyz;
    output.Local = input.Pos;
    output.Plane = input.Plane;

    return output;
}

float SlotOf(int plane)
{
    float4 group = Slots[plane >> 2];
    int lane = plane & 3;

    return dot(group, float4(lane == 0, lane == 1, lane == 2, lane == 3));
}

int FaceOf(float3 ray)
{
    float3 size = abs(ray);

    if (size.x >= size.y && size.x >= size.z)
        return ray.x >= 0.0 ? 0 : 1;

    if (size.y >= size.z)
        return ray.y >= 0.0 ? 2 : 3;

    return ray.z >= 0.0 ? 4 : 5;
}

float Hash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float Noise(float2 p)
{
    float2 cell = floor(p);
    float2 gap = frac(p);
    float2 eased = gap * gap * (3.0 - 2.0 * gap);

    float low = lerp(Hash(cell), Hash(cell + float2(1.0, 0.0)), eased.x);
    float high = lerp(Hash(cell + float2(0.0, 1.0)), Hash(cell + float2(1.0, 1.0)), eased.x);

    return lerp(low, high, eased.y);
}

float2 RippleOffset(float3 local)
{
    if (RippleAmount <= 0.0)
        return 0.0;

    float2 p = float2(local.x + local.z * 0.5, local.y + local.z * 0.8) * RippleScale;
    float2 wave = float2(Noise(p + RipplePhase), Noise(p.yx - RipplePhase + 17.0)) * 2.0 - 1.0;

    return wave * RippleAmount;
}

float3 Unproject(float2 uv, float depth, matrix inverse)
{
    float4 position = mul(float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0), inverse);

    return position.xyz / position.w;
}

float4 PSMain(PS_IN input) : SV_TARGET
{
    float3 normal = normalize(input.Nrm);
    float3 toEye = normalize(CameraPosition.xyz - input.World);

    if (dot(normal, toEye) < 0.0)
    {
        if (IsTwoSided < 0.5 && dot(normal, toEye) < BackFacing)
            discard;

        normal = -normal;
    }

    float facing = saturate(dot(normal, toEye));
    float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
    float strength = Strength * lerp(1.0, fresnel, FresnelRate);
    float mask = HasMask > 0.5 ? MaskTexture.Sample(LinearSampler, float2(input.Local.x + 0.5, 0.5 - input.Local.y)).a : 1.0;
    float2 ripple = RippleOffset(input.Local);

    int plane = (int)round(input.Plane);
    float slot = plane >= 0 ? SlotOf(plane) : CubeSlot;

    float4 color;
    float3 hit;

    if (slot < CubeSlot - 0.5)
    {
        float2 screen = (input.Pos.xy - ViewportStart) / ViewportSize + ripple;
        float3 at = float3(saturate(screen) * PlanarScale.xy, slot);

        color = PlanarColor.SampleLevel(LinearSampler, at, PlanarMip);
        hit = Unproject(screen, PlanarDepth.SampleLevel(PointSampler, at, 0.0), PlaneInverse[(int)slot]);
    }
    else
    {
        if (HasCube < 0.5)
            discard;

        float3 ray = normalize(reflect(-toEye, normal) + float3(ripple, ripple.x));
        int face = FaceOf(ray);
        float4 clip = mul(float4(CubeCenter.xyz + ray, 1.0), FaceViewProjection[face]);
        float2 uv = saturate(clip.xy / clip.w * float2(0.5, -0.5) + 0.5);
        float3 at = float3(uv, face);

        color = CubeColor.SampleLevel(LinearSampler, at, CubeMip);
        hit = Unproject(uv, CubeDepth.SampleLevel(PointSampler, at, 0.0), FaceInverse[face]);
    }

    color = Unpremultiply(color);

    float reach = ReachDistance > 0.0 ? saturate(1.0 - distance(hit, input.World) / ReachDistance) : 1.0;
    float alpha = saturate(color.a * strength * mask * reach * Opacity);

    return float4(color.rgb * TintColor.rgb, alpha);
}
