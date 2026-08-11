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
        /// <summary>テクスチャの一辺の上限（ピクセル）。D3D11 の仕様上の最大値。</summary>
        public const int MaxTextureSize = 16384;

        private static readonly RawRectF Unknown = new(0, 0, 1, 1);

        public static RawRectF Get(ID2D1DeviceContext? deviceContext, ID2D1Image image)
        {
            if (image is ID2D1Bitmap bitmap)
            {
                var size = bitmap.Size;
                return new RawRectF(0, 0, size.Width, size.Height);
            }

            // 破棄済みの参照を渡すと、vtable 経由の呼び出しがアクセス違反になる。
            // これは catch できず、プロセスごと落ちる。
            if (deviceContext is null
                || deviceContext.NativePointer == nint.Zero
                || image.NativePointer == nint.Zero)
            {
                return Unknown;
            }

            // コンテキストはスレッド安全ではない。範囲の問い合わせも内部状態を触るため、
            // 他の Direct2D 操作と重ならないようにする。
            lock (D2DGate.Sync)
            {
                try
                {
                    var bounds = deviceContext.GetImageLocalBounds(image);
                    return IsUsable(bounds) ? bounds : Unknown;
                }
                catch
                {
                    // エフェクトによっては範囲が確定できず例外になる。
                }
            }

            return Unknown;
        }

        /// <summary>
        /// 描画範囲として意味のある値かどうかを調べます。
        /// </summary>
        /// <remarks>
        /// 範囲が確定できない画像に対しては、無限大や NaN が返ることがあります。
        /// そのまま計算に使うと、テクスチャの大きさが負や桁外れの値になり、
        /// 確保の時点で落ちます。
        /// </remarks>
        private static bool IsUsable(in RawRectF bounds)
            => float.IsFinite(bounds.Left) && float.IsFinite(bounds.Top)
            && float.IsFinite(bounds.Right) && float.IsFinite(bounds.Bottom)
            && bounds.Right >= bounds.Left && bounds.Bottom >= bounds.Top;

        /// <summary>
        /// 描画範囲を、テクスチャに必要なピクセル数へ切り上げます。
        /// </summary>
        /// <remarks>
        /// 確保できない大きさを返さないよう、<see cref="MaxTextureSize"/> で頭打ちにします。
        /// </remarks>
        public static (int Width, int Height) ToPixelSize(in RawRectF bounds) => (
            ToPixels(bounds.Right - bounds.Left),
            ToPixels(bounds.Bottom - bounds.Top));

        private static int ToPixels(float length)
        {
            if (!float.IsFinite(length))
                return 1;

            return (int)Math.Clamp(Math.Ceiling(length), 1, MaxTextureSize);
        }
    }
}
