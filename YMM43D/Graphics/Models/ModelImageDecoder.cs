using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YMM43D.Graphics.Models
{
    public static class ModelImageDecoder
    {
        public static ModelImage Decode(Stream stream)
        {
            var decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            var frame = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);

            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var pixels = new byte[width * height * 4];

            converted.CopyPixels(pixels, width * 4, 0);

            return new ModelImage(width, height, pixels);
        }

        public static ModelImage Decode(byte[] data)
        {
            using var stream = new MemoryStream(data, writable: false);
            return Decode(stream);
        }

        public static ModelImage DecodeFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Decode(stream);
        }
    }
}
