#include "PointCloud.hlsli"

float3 Hash(float3 cell)
{
    float3 p = cell + Seed * 17.13;
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx) * 2.0 - 1.0;
}

float3 Ratio(float3 cell)
{
    return GridCount > 1.5 ? cell / max(GridCount - 1.0, 1.0) : 0.5;
}

float3 Deform(float3 p)
{
    if (DeformKind < 0.5)
        return p;

    float along = dot(p, DeformAxis);
    float halfSpan = max(dot(abs(DeformAxis), Extent) * 0.5, 1e-4);
    float3 across = p - DeformAxis * along;

    if (DeformKind < 1.5)
    {
        float3 side = abs(DeformAxis.z) > 0.5 ? float3(1, 0, 0) : float3(0, 0, 1);

        return p + side * (DeformAmount * sin(2.0 * Pi * along / DeformPeriod + DeformPhase));
    }

    if (DeformKind < 2.5)
    {
        float angle = DeformAmount * (along / halfSpan) + DeformPhase;

        return DeformAxis * along
             + across * cos(angle)
             + cross(DeformAxis, across) * sin(angle);
    }

    if (DeformKind < 3.5)
    {
        float limit = max(length(Extent - abs(DeformAxis) * Extent) * 0.5, 1e-4);
        float ratio = saturate(length(across) / limit);

        return p + DeformAxis * (DeformAmount * (1.0 - ratio * ratio));
    }

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

    float3 local = float3(
         (ratio.x - 0.5) * Extent.x,
        -(ratio.y - 0.5) * Extent.y,
         (ratio.z - 0.5) * Extent.z);

    return Deform(local);
}

float3 Place(float3 cell)
{
    return Shape(cell) + Hash(cell) * Scatter;
}

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

    output.Random = float2(
        Hash(input.Cell).x,
        Hash(input.Cell + input.Other).y) * 0.5 + 0.5;

    output.Edge = input.Corner;

    float3 local = Place(input.Cell);

    output.Shading = 0.0;

    if (any(input.Other != input.Cell))
    {
        if (output.Random.y < LineRandomness)
        {
            output.Position = float4(0, 0, -1, 1);
            output.Nrm = float3(0, 0, 0);
            output.World = float3(0, 0, 0);
            return output;
        }

        float3 along = Place(input.Other) - local;
        float3 side = cross(along, ViewForward);
        float length2 = dot(side, side);

        if (length2 > 1e-12)
            local += normalize(side) * input.Corner.x * LineHalfWidth;

        output.Shading = 1.0;
    }
    else if (any(input.Corner != 0.0))
    {
        local += (ViewRight * input.Corner.x + ViewUp * input.Corner.y) * PointHalfSize;

        output.Shading = PointIsRound > 0.5 ? 1.0 : 2.0;
    }

    output.Position = mul(float4(local, 1.0), WorldViewProjection);
    output.Nrm = mul(float4(SurfaceNormal(input.Cell), 0.0), WorldInverse).xyz;
    output.World = mul(float4(local, 1.0), World).xyz;
    return output;
}
