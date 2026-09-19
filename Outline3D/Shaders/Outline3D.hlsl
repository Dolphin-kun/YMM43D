cbuffer OutlineConstants : register(b2)
{
    float4 Region;
    float4 Target;
    float4 Inverse;
    float4 Placement;
    float4 Stroke;
    float4 Shape;
    float4 StrokeColor;
    float4 BrushArea;
    float4 DepthMap;
};

Texture2D Mask : register(t4);
Texture2D<float> MaskDepth : register(t5);
Texture2D<float4> Seeds : register(t6);
Texture2D BrushImage : register(t7);
SamplerState LinearClamp : register(s3);

static const float Unset = 1e20;

static const float Covered = 0.02;

float4 VSMain(uint id : SV_VertexID) : SV_Position
{
    float2 corner = float2(id & 1, (id >> 1) & 1);
    float2 pixel = lerp(Region.xy, Region.zw, corner);
    float2 ndc = pixel / Target.zw * float2(2, -2) + float2(-1, 1);

    return float4(ndc, 0, 1);
}

float Measure(float2 v)
{
    if (Shape.x > 0.5)
        return max(abs(v.x), abs(v.y));

    float len = length(v);

    if (len < 1e-4)
        return 0;

    float sector = 6.2831853 / Shape.y;
    float angle = atan2(v.y, v.x);
    float face = (floor(angle / sector) + 0.5) * sector;

    return len * cos(angle - face) / Shape.z;
}

float2 ToSource(float2 p)
{
    float2 v = p - Placement.xy - Placement.zw;

    return Placement.xy + float2(Inverse.x * v.x + Inverse.y * v.y, Inverse.z * v.x + Inverse.w * v.y);
}

bool IsDrawn(float2 p)
{
    return MaskDepth.Load(int3(p, 0)) < 1;
}

bool IsInterior(float2 p)
{
    return IsDrawn(p)
        && IsDrawn(p + float2(1, 0)) && IsDrawn(p - float2(1, 0))
        && IsDrawn(p + float2(0, 1)) && IsDrawn(p - float2(0, 1));
}

bool InRegion(float2 p)
{
    return all(p >= Region.xy) && all(p < Region.zw);
}

float4 SeedPS(float4 position : SV_Position) : SV_Target
{
    float2 source = ToSource(position.xy);

    if (any(source < 0) || any(source >= Target.zw))
        return 0;

    if (Mask.SampleLevel(LinearClamp, source / Target.zw, 0).a < Covered)
        return 0;

    return float4(position.xy, MaskDepth.Load(int3(source, 0)), 1);
}

float4 JumpPS(float4 position : SV_Position) : SV_Target
{
    float2 p = position.xy;
    float4 best = Seeds.Load(int3(p, 0));
    float bestGap = best.w > 0 ? Measure(p - best.xy) : Unset;

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 neighbor = p + float2(x, y) * Stroke.w;

            if ((x == 0 && y == 0) || !InRegion(neighbor))
                continue;

            float4 seed = Seeds.Load(int3(neighbor, 0));

            if (seed.w <= 0)
                continue;

            float gap = Measure(p - seed.xy);

            if (gap < bestGap)
            {
                bestGap = gap;
                best = seed;
            }
        }
    }

    return best;
}

float Behind(float depth)
{
    float m33 = DepthMap.x;
    float m34 = DepthMap.y;
    float m43 = DepthMap.z;
    float bias = DepthMap.w;

    if (abs(m34) < 1e-6)
        return saturate(depth + bias * m33);

    float viewZ = m43 / (depth * m34 - m33);
    viewZ += bias * sign(m34);

    return saturate((viewZ * m33 + m43) / (viewZ * m34));
}

struct CompositeOutput
{
    float4 Color : SV_Target;
    float Depth : SV_Depth;
};

CompositeOutput CompositePS(float4 position : SV_Position)
{
    float2 p = position.xy - Target.xy;
    float4 seed = Seeds.Load(int3(p, 0));

    if (seed.w <= 0)
        discard;

    if (IsInterior(p))
        discard;

    float edge = Stroke.x + 0.5;
    float gap = Measure(p - seed.xy);

    float alpha = Stroke.y > 0
        ? 1.0 / (1.0 + exp(1.702 * (gap - edge) / Stroke.y))
        : saturate((edge - gap) / Stroke.z + 0.5);

    if (Shape.w > 0.5)
        alpha *= 1 - Mask.Load(int3(p, 0)).a;

    float4 color = StrokeColor;

    if (BrushArea.w > 0.5)
    {
        float2 local = (ToSource(p) - Placement.xy) / BrushArea.x;
        float4 texel = BrushImage.SampleLevel(LinearClamp, local / BrushArea.yz + 0.5, 0);

        color.rgb *= texel.a > 0 ? texel.rgb / texel.a : 0;
        color.a *= texel.a;
    }

    alpha *= color.a;

    if (alpha < 1.0 / 512.0)
        discard;

    CompositeOutput output;
    output.Color = float4(color.rgb, alpha);
    output.Depth = Behind(seed.z);

    return output;
}
