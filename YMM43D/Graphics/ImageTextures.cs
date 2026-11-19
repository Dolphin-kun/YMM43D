using Vortice.Direct3D11;
using YMM43D.Graphics.Models;

namespace YMM43D.Graphics
{
    public sealed class ImageTextures(ID3D11Device device) : IDisposable
    {
        private const int MaxKept = 32;

        private readonly List<(ModelImage Image, ID3D11Texture2D Texture, ID3D11ShaderResourceView View, int Levels)> kept = [];

        private ID3D11Texture2D? whiteTexture;
        private ID3D11ShaderResourceView? white;

        public (ID3D11ShaderResourceView? View, float Levels) Get(ID3D11DeviceContext context, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || ModelLibrary.FindImage(path) is not { } image)
                return (null, 0f);

            var at = kept.FindIndex(one => ReferenceEquals(one.Image, image));

            if (at >= 0)
            {
                var found = kept[at];

                kept.RemoveAt(at);
                kept.Add(found);

                return (found.View, found.Levels);
            }

            if (ModelTexture.Create(device, context, image) is not { } made)
                return (null, 0f);

            kept.Add((image, made.Texture, made.View, made.Levels));

            while (kept.Count > MaxKept)
            {
                kept[0].View.Dispose();
                kept[0].Texture.Dispose();
                kept.RemoveAt(0);
            }

            return (made.View, made.Levels);
        }

        public ID3D11ShaderResourceView White(ID3D11DeviceContext context)
        {
            if (white is not null)
                return white;

            whiteTexture = device.CreateTexture2D(new Texture2DDescription
            {
                Width = 1,
                Height = 1,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
            });

            context.UpdateSubresource(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, whiteTexture, 0, 4);

            return white = device.CreateShaderResourceView(whiteTexture);
        }

        public void Dispose()
        {
            foreach (var one in kept)
            {
                one.View.Dispose();
                one.Texture.Dispose();
            }

            kept.Clear();
            white?.Dispose();
            white = null;
            whiteTexture?.Dispose();
            whiteTexture = null;
        }
    }
}
