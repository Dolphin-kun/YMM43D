cbuffer GridConstants : register(b0)
{
    matrix WorldViewProjection;
    float4 CameraPosition;
};

struct VS_IN  { float3 Pos : POSITION; float4 Col : COLOR; float2 Tex : TEXCOORD; };
struct PS_IN  { float4 Pos : SV_POSITION; float3 WorldPos : WORLDPOS; };

PS_IN VSMain(VS_IN input)
{
    PS_IN output;
    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
    output.WorldPos = input.Pos;
    return output;
}

float GridLine(float position, float width)
{
    float distanceToLine = abs(frac(position - 0.5) - 0.5);
    return 1.0 - smoothstep(0, width, distanceToLine);
}

float4 PSMain(PS_IN input) : SV_TARGET
{
    float3 pos = input.WorldPos;

    float alpha = GridLine(pos.x, 0.03) + GridLine(pos.z, 0.03);
    float4 color = float4(0.2, 0.2, 0.2, 1.0);

    bool onXAxis = abs(pos.z) < 0.05;
    bool onZAxis = abs(pos.x) < 0.05;
    if (onXAxis)      color = float4(0.8, 0.1, 0.1, 1.0);
    else if (onZAxis) color = float4(0.15, 0.35, 0.95, 1.0);
    if (onXAxis || onZAxis) alpha = 1.0;

    if (alpha <= 0.0) discard;

    float distance = length(pos.xz - CameraPosition.xz);
    float fade = 1.0 - smoothstep(10.0, 100.0, distance);

    return float4(color.rgb, alpha * fade * 0.5);
}
