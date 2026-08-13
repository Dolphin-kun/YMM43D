using Vortice;
using Vortice.Direct2D1;

namespace YMM43D.Player
{
    public static class D2DImageBounds
    {
        public const int MaxTextureSize = 16384;

        private static readonly RawRectF Unknown = new(0, 0, 1, 1);

        public static RawRectF Get(ID2D1DeviceContext? deviceContext, ID2D1Image image)
        {
            if (image is ID2D1Bitmap bitmap)
            {
                var size = bitmap.Size;
                return new RawRectF(0, 0, size.Width, size.Height);
            }

            if (deviceContext is null
                || deviceContext.NativePointer == nint.Zero
                || image.NativePointer == nint.Zero)
            {
                return Unknown;
            }

            lock (D2DGate.Sync)
            {
                try
                {
                    var bounds = deviceContext.GetImageLocalBounds(image);
                    return IsUsable(bounds) ? bounds : Unknown;
                }
                catch
                {
                }
            }

            return Unknown;
        }

        private static bool IsUsable(in RawRectF bounds)
            => float.IsFinite(bounds.Left) && float.IsFinite(bounds.Top)
            && float.IsFinite(bounds.Right) && float.IsFinite(bounds.Bottom)
            && bounds.Right >= bounds.Left && bounds.Bottom >= bounds.Top;

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
