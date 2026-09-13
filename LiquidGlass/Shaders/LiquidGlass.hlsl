Texture2D InputTexture : register(t0);
Texture2D BlurTexture : register(t1);
SamplerState InputSampler : register(s0);
SamplerState BlurSampler : register(s1);

cbuffer Constants : register(b0)
{
    float4 Placement;
    float4 Form;
    float4 Glass;
    float4 Tint;
    float4 Light;
};

#define Center        Placement.xy
#define HalfSize      Placement.zw
#define CornerRadius  Form.x
#define Rotation      Form.y
#define IsEllipse     Form.z
#define Bevel         Form.w
#define Refraction    Glass.x
#define Dispersion    Glass.y
#define Brightness    Glass.z
#define ShowsOutside  Glass.w
#define TintAmount    Tint.a
#define Highlight     Light.x
#define LightAngle    Light.y
#define ShadowAmount  Light.z
#define ShadowSpread  Light.w

float2 ToLocal(float2 scene)
{
    float2 p = scene - Center;
    float c = cos(Rotation);
    float s = sin(Rotation);

    return float2(c * p.x + s * p.y, -s * p.x + c * p.y);
}

float2 ToScene(float2 direction)
{
    float c = cos(Rotation);
    float s = sin(Rotation);

    return float2(c * direction.x - s * direction.y, s * direction.x + c * direction.y);
}

float SignedDistance(float2 q)
{
    float2 extent = max(HalfSize, 0.001);

    if (IsEllipse > 0.5)
    {
        float k = length(q / extent);
        return (k - 1.0) * min(extent.x, extent.y);
    }

    float radius = min(CornerRadius, min(extent.x, extent.y));
    float2 d = abs(q) - extent + radius;

    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
}

float2 Outward(float2 q)
{
    const float e = 0.5;

    float2 gradient = float2(
        SignedDistance(q + float2(e, 0)) - SignedDistance(q - float2(e, 0)),
        SignedDistance(q + float2(0, e)) - SignedDistance(q - float2(0, e)));

    float size = length(gradient);

    return size > 1e-6 ? ToScene(gradient / size) : float2(0, 0);
}

float4 SampleBlur(float4 uv, float2 offset)
{
    return BlurTexture.SampleLevel(BlurSampler, uv.xy + offset * uv.zw, 0);
}

float4 main(
    float4 position : SV_POSITION,
    float4 scene : SCENE_POSITION,
    float4 uv0 : TEXCOORD0,
    float4 uv1 : TEXCOORD1) : SV_Target
{
    float4 original = ShowsOutside > 0.5 ? InputTexture.SampleLevel(InputSampler, uv0.xy, 0) : float4(0, 0, 0, 0);

    float2 q = ToLocal(scene.xy);
    float gap = SignedDistance(q);

    if (gap > ShadowSpread + 1.0)
        return original;

    float shadow = gap > 0.0 && ShadowSpread > 0.0
        ? ShadowAmount * pow(saturate(1.0 - gap / ShadowSpread), 2.0)
        : (gap > 0.0 ? 0.0 : ShadowAmount);

    float4 below = float4(original.rgb * (1.0 - shadow), shadow + original.a * (1.0 - shadow));

    float coverage = saturate(0.5 - gap);

    if (coverage <= 0.0)
        return below;

    float edge = 1.0 - saturate(-gap / Bevel);
    float2 normal = Outward(q);

    float2 offset = normal * Refraction * edge * edge;

    float4 center = SampleBlur(uv1, offset);
    float red = SampleBlur(uv1, offset * (1.0 + Dispersion)).r;
    float blue = SampleBlur(uv1, offset * max(1.0 - Dispersion, 0.0)).b;

    float3 color = float3(red, center.g, blue) * Brightness;
    float alpha = center.a;

    color = Tint.rgb * TintAmount + color * (1.0 - TintAmount);
    alpha = TintAmount + alpha * (1.0 - TintAmount);

    float2 toward = float2(cos(LightAngle), sin(LightAngle));
    float rim = edge * edge * edge;
    float shine = Highlight * rim * (0.25 + 0.75 * saturate(dot(normal, toward)) + 0.4 * saturate(dot(normal, -toward)));

    color = min(color + shine, 1.0);
    alpha = saturate(max(alpha + shine, max(color.r, max(color.g, color.b))));

    float4 glass = float4(color, alpha);

    return lerp(below, glass, coverage);
}
