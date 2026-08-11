using Vortice;
using Vortice.Direct2D1;

namespace YMM43D.Integration
{
    /// <summary>
    /// <see cref="ID2D1Image"/> の大きさを調べるヘルパー。
    /// </summary>
    public static class D2DImageBounds
    {
        /// <summary>
        /// 画像の描画範囲を取得します。
        /// </summary>
        /// <remarks>
        /// ビットマップは自分でサイズを持っていますが、エフェクトの出力などは
        /// デバイスコンテキストに問い合わせないと範囲が分かりません。
        /// どちらでも失敗した場合は 1×1 を返します。
        /// </remarks>
        public static RawRectF Get(ID2D1DeviceContext? deviceContext, ID2D1Image image)
        {
            if (image is ID2D1Bitmap bitmap)
            {
                var size = bitmap.Size;
                return new RawRectF(0, 0, size.Width, size.Height);
            }

            if (deviceContext is not null)
            {
                try
                {
                    return deviceContext.GetImageLocalBounds(image);
                }
                catch
                {
                    // エフェクトによっては範囲が確定できず例外になる。
                }
            }

            return new RawRectF(0, 0, 1, 1);
        }

        /// <summary>
        /// 描画範囲を、テクスチャに必要なピクセル数へ切り上げます。
        /// </summary>
        public static (int Width, int Height) ToPixelSize(in RawRectF bounds) => (
            (int)Math.Max(1, Math.Ceiling(bounds.Right - bounds.Left)),
            (int)Math.Max(1, Math.Ceiling(bounds.Bottom - bounds.Top)));
    }
}
