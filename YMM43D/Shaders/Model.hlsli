#ifndef YMM43D_MODEL_HLSLI
#define YMM43D_MODEL_HLSLI

#include "Lighting.hlsli"
#include "Texture.hlsli"

Texture2D tex       : register(t0);
Texture2D bumpMap   : register(t5);
Texture2D glowMap   : register(t6);
Texture2D feelMap   : register(t7);
Texture2D shadeMap  : register(t8);
Texture2D mirrorMap : register(t9);

SamplerState samp : register(s0);

cbuffer ModelConstants : register(b1)
{
    float4 Uv;
    float4 Bump;
    float4 Glow;
    float4 Shade;
    float4 Feel;
    float4 Mirror;
    float4 Tune;
};

#define UvScale       Uv.xy
#define UvOffset      Uv.zw
#define UvSpin        Bump.x
#define BumpScale     Bump.y
#define HasBump       Bump.z
#define HasGlow       Glow.a
#define HasShade      Shade.a
#define Metallic      Feel.x
#define Roughness     Feel.y
#define HasFeel       Feel.z
#define MirrorAmount  Mirror.x
#define MirrorLevels  Mirror.y
#define MirrorAmbient Mirror.z
#define HasMirror     Mirror.w
#define MetallicAdd   Tune.x
#define Smoothness    Tune.y

struct MODEL_IN
{
    float3 Pos : POSITION;
    float4 Col : COLOR;
    float2 Tex : TEXCOORD0;
    float3 Nrm : NORMAL;
    float4 Tan : TANGENT;
};

struct MODEL_PS
{
    float4 Pos   : SV_POSITION;
    float4 Col   : COLOR;
    float2 Tex   : TEXCOORD0;
    float3 Nrm   : NORMAL;
    float3 World : TEXCOORD1;
    float4 Tan   : TANGENT;
};

MODEL_PS MakeModelVertex(float3 position, float4 color, float2 texCoord, float3 normal, float4 tangent)
{
    MODEL_PS output;
    output.Pos = mul(float4(position, 1.0), WorldViewProjection);
    output.Col = color;
    output.Tex = texCoord;
    output.Nrm = mul(float4(normal, 0.0), WorldInverse).xyz;
    output.World = mul(float4(position, 1.0), World).xyz;
    output.Tan = float4(mul(float4(tangent.xyz, 0.0), World).xyz, tangent.w);
    return output;
}

float2 PlaceUv(float2 uv)
{
    float2 scaled = uv * UvScale;

    if (UvSpin == 0.0)
        return scaled + UvOffset;

    float s = sin(UvSpin);
    float c = cos(UvSpin);

    return float2(c * scaled.x + s * scaled.y, c * scaled.y - s * scaled.x) + UvOffset;
}

float3 BumpNormal(float3 n, float4 tangent, float2 uv)
{
    if (HasBump < 0.5 || dot(tangent.xyz, tangent.xyz) < 1e-8)
        return n;

    float3 t = tangent.xyz - n * dot(n, tangent.xyz);

    if (dot(t, t) < 1e-8)
        return n;

    t = normalize(t);

    float3 b = cross(n, t) * (tangent.w < 0.0 ? -1.0 : 1.0);
    float3 read = bumpMap.Sample(samp, uv).rgb * 2.0 - 1.0;

    read.xy *= BumpScale;

    return normalize(t * read.x + b * read.y + n * read.z);
}

float2 MirrorUv(float3 direction)
{
    float3 d = normalize(direction);

    return float2(
        atan2(d.x, -d.z) * 0.15915494 + 0.5,
        acos(clamp(d.y, -1.0, 1.0)) * 0.31830989);
}

float3 MirrorAt(float3 direction, float level)
{
    return mirrorMap.SampleLevel(samp, MirrorUv(direction), level).rgb;
}

float4 ShadeModel(MODEL_PS input)
{
    float3 worldDx = ddx(input.World);
    float3 worldDy = ddy(input.World);

    float2 uv = PlaceUv(input.Tex);
    float4 color = input.Col * Unpremultiply(tex.Sample(samp, uv));

    clip(color.a - AlphaCutoff);

    float3 lit = color.rgb;

    float3 shade = HasShade > 0.5
        ? input.Col.rgb * Unpremultiply(shadeMap.Sample(samp, uv)).rgb * Shade.rgb
        : lit * Shade.rgb;

    float3 glow = Glow.rgb;

    if (HasGlow > 0.5)
        glow *= Unpremultiply(glowMap.Sample(samp, uv)).rgb;

    if (Unlit > 0.5 || dot(input.Nrm, input.Nrm) < 1e-8)
    {
        color.rgb = ApplyFog(lit + glow, input.World);
        color.a *= Opacity;
        return color;
    }

    float metallic = Metallic;
    float roughness = Roughness;

    if (HasFeel > 0.5)
    {
        float4 feel = feelMap.Sample(samp, uv);

        roughness *= feel.g;
        metallic *= feel.b;
    }

    metallic = saturate(metallic + MetallicAdd);
    roughness = saturate(roughness - Smoothness);

    float3 toEye;
    float3 n = BumpNormal(FaceCamera(input.Nrm, input.World, toEye), input.Tan, uv);

    float3 sum;
    float3 shine;
    GatherLight(n, input.World, worldDx, worldDy, toEye, sum, shine);

    float3 ambient = AmbientOf(n);

    if (HasMirror > 0.5 && MirrorAmbient > 0.0)
    {
        float3 around = MirrorAt(n, max(MirrorLevels - 3.0, 0.0));

        ambient *= lerp(float3(1.0, 1.0, 1.0), around * 2.0, MirrorAmbient);
    }

    float3 result = shade * ambient + lit * sum + shine * Gloss;

    if (HasMirror > 0.5 && MirrorAmount > 0.0)
    {
        float3 seen = MirrorAt(reflect(-toEye, n), saturate(roughness) * max(MirrorLevels - 1.0, 0.0));

        float facing = 1.0 - saturate(dot(n, toEye));
        float fresnel = 0.04 + 0.96 * pow(facing, 5.0);
        float amount = saturate(MirrorAmount * lerp(fresnel, 1.0, metallic));

        result = lerp(result, seen * lerp(float3(1.0, 1.0, 1.0), lit, metallic), amount);
    }

    color.rgb = ApplyFog(result + glow, input.World);
    color.a *= Opacity;
    return color;
}

#endif
