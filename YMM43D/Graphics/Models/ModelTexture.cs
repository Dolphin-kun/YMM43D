using Vortice.DXGI;
using Vortice.Direct3D11;

namespace YMM43D.Graphics.Models
{
    internal static class ModelTexture
    {
        public static (ID3D11Texture2D Texture, ID3D11ShaderResourceView View, int Levels)? Create(
            ID3D11Device device, ID3D11DeviceContext context, ModelImage image)
        {
            if (image.Width <= 0 || image.Height <= 0)
                return null;

            var texture = device.CreateTexture2D(new Texture2DDescription
            {
                Width = image.Width,
                Height = image.Height,
                MipLevels = 0,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                MiscFlags = ResourceOptionFlags.GenerateMips,
            });

            context.UpdateSubresource(image.Pixels, texture, 0, image.Width * 4);

            var view = device.CreateShaderResourceView(texture);

            context.GenerateMips(view);

            return (texture, view, LevelsOf(image.Width, image.Height));
        }

        public static int LevelsOf(int width, int height)
        {
            var size = Math.Max(width, height);
            var levels = 1;

            while (size > 1)
            {
                size /= 2;
                levels++;
            }

            return levels;
        }
    }
}
