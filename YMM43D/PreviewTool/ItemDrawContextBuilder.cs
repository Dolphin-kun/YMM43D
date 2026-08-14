using System.Numerics;
using Vortice;
using Vortice.Direct3D11;
using YMM43D.Player;
using YMM43D.Plugin;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class ItemDrawContextBuilder : IDisposable
    {
        private readonly ItemRenderPipeline pipeline = new();
        private readonly D2DTextureBridge textureBridge = new();

        public DrawContext3D Build(
            IVideoItem item,
            in FrameContext itemTime,
            PreviewEnvironment environment,
            I3DProvider provider)
        {
            var providerTexture = provider is I3DTextureProvider textureProvider
                ? textureProvider.GetTexture(environment.Device)
                : null;

            var needsImage = provider.RequiresMappedTexture && providerTexture is null;
            var rendered = pipeline.Render(item, itemTime, environment, needsImage);

            var texture = providerTexture;
            RawRectF? imageBounds = null;
            if (texture is null && needsImage && rendered.Image is { } image)
            {
                texture = textureBridge.GetTexture(
                    environment.Device, environment.Devices, image, item, out var bounds);

                if (texture is not null)
                    imageBounds = bounds;
            }

            return new DrawContext3D
            {
                World = BuildSizeMatrix(provider, imageBounds)
                      * ItemPlacement.GetWorldMatrix(item, itemTime, rendered.CameraMatrix),
                Opacity = Math.Clamp(ItemPlacement.GetOpacity(item, itemTime), 0f, 1f),
                Blend = ToBlendMode(item.Blend),
                IsAlwaysOnTop = item.IsAlwaysOnTop,
                Time = itemTime,
                Texture = texture,
            };
        }

        // タイムラインの並びが変わると、YMM4 側が組み直した合成の上に、こちらが
        // 前のまま掴んでいる画像が残る。触れば解放済みの領域へ飛ぶので、
        // 作り直すきっかけをもらったら、抱えているものは全部捨てる。
        public void Reset()
        {
            pipeline.Clear();
            textureBridge.Clear();
        }

        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems)
        {
            pipeline.RetainOnly(aliveItems);

            textureBridge.RetainOnly(aliveItems.Cast<object>().ToHashSet());
        }

        public void Dispose()
        {
            pipeline.Dispose();
            textureBridge.Dispose();
        }

        private static Matrix4x4 BuildSizeMatrix(I3DProvider provider, RawRectF? imageBounds)
        {
            if (provider is I3DSizeProvider sizeProvider)
            {
                if (!sizeProvider.ScalesToInputSize)
                    return Matrix4x4.Identity;

                if (sizeProvider.TryGetSize(out var size, out var offset))
                    return WorldScale.CreateSizeMatrix(size, offset + size / 2f);
            }

            if (imageBounds is { } bounds)
            {
                return WorldScale.CreateSizeMatrix(
                    new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
                    new Vector2((bounds.Left + bounds.Right) / 2f, (bounds.Top + bounds.Bottom) / 2f));
            }

            return Matrix4x4.Identity;
        }

        private static Graphics.BlendMode ToBlendMode(YukkuriMovieMaker.Project.Blend blend) => blend switch
        {
            YukkuriMovieMaker.Project.Blend.Add => Graphics.BlendMode.Add,
            YukkuriMovieMaker.Project.Blend.Subtract => Graphics.BlendMode.Subtract,
            YukkuriMovieMaker.Project.Blend.Multiply => Graphics.BlendMode.Multiply,
            YukkuriMovieMaker.Project.Blend.Screen => Graphics.BlendMode.Screen,
            _ => Graphics.BlendMode.Normal,
        };
    }

    internal readonly record struct PreviewEnvironment(
        ID3D11Device Device,
        IGraphicsDevicesAndContext Devices,
        Scene? Scene,
        TimelineSourceDescription? SourceDescription);
}
