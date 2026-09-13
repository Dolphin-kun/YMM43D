#ifndef YMM43D_TEXTURE_HLSLI
#define YMM43D_TEXTURE_HLSLI

float4 Unpremultiply(float4 color)
{
    return float4(color.a > 0.0 ? color.rgb / color.a : color.rgb, color.a);
}

#endif
