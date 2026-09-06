#include "PointCloud.hlsli"

Texture2D    txDiffuse : register(t0);
SamplerState samLinear : register(s0);

// 形の縁がどれだけこの画素を覆っているかを返す。描画先はマルチサンプル
// ではないので、これを掛けないと粒や線の縁が階段状になる。
//
// 1画素より細い線は、太さを保ったままギザギザに描かれるのではなく、
// 薄くなって消えていく。面は edge が動かないので常に 1 が返り、
// 隣り合う三角形の継ぎ目に隙間ができない。
float Coverage(float2 edge)
{
    if (PointIsRound > 0.5)
    {
        float radius = length(edge);
        return saturate((1.0 - radius) / max(fwidth(radius), 1e-6));
    }

    float2 width = fwidth(edge);
    float2 coverage = saturate((1.0 - abs(edge)) / max(width, 1e-6));

    return min(coverage.x, coverage.y);
}

float4 main(PS_INPUT input) : SV_Target
{
    // 捨てたあとの画素は隣との差が定まらないので、割合は捨てる前に求める。
    float coverage = Coverage(input.Edge);

    float4 source = Unpremultiply(txDiffuse.SampleLevel(samLinear, input.TexCoord, 0));

    if (source.a < Threshold)
        discard;

    float scatter = lerp(1.0, input.Random.x, OpacityRandomness);
    float alpha = Color.a * Opacity * ExtraOpacity * scatter * coverage;

    // 透けている画素も、捨てなければ深度は書いてしまう。円い粒では
    // 四角い板の隅がそのまま残り、後ろの粒を隠して黒く抜けて見える。
    if (alpha < 1.0 / 255.0)
        discard;

    float3 rgb = UseSourceColor > 0.5 ? source.rgb : Color.rgb;

    float3 shape = input.Shading > 1.5 ? -ViewForward
                 : input.Shading > 0.5 ? Billboard(input.Edge)
                 : float3(0, 0, 0);

    float3 normal = any(shape != 0.0)
        ? mul(float4(shape, 0.0), WorldInverse).xyz
        : input.Nrm;

    rgb = ApplyLight(rgb, normal, input.World);
    rgb = ApplyFog(rgb, input.World);

    return float4(rgb, alpha);
}
