#ifndef YMM43D_TEXTURE_HLSLI
#define YMM43D_TEXTURE_HLSLI

// Direct2D から受け取る画像は、色にあらかじめ不透明度を掛けた形で入っている。
// 陰影も霧も混ぜ合わせも「掛かっていない色」を前提にしているので、割り戻す。
//
// 割り戻さないと不透明度がもう一度掛かる。登場や退場で薄くしたとき、
// 色まで一緒に沈んでいき、消え際が黒ずんで見える。
float4 Unpremultiply(float4 color)
{
    return float4(color.a > 0.0 ? color.rgb / color.a : color.rgb, color.a);
}

#endif
