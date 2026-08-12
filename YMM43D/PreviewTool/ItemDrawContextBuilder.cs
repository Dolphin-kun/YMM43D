using System.Numerics;
using Vortice;
using Vortice.Direct3D11;
using YMM43D.Integration;
using YMM43D.Plugin;
using YMM43D.Scene3D;
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
            // プロバイダーが自前のテクスチャを持つ場合、アイテムの画像は要らない。
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

        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems)
        {
            pipeline.RetainOnly(aliveItems);

            // テクスチャの鍵もアイテム。手放さないと、消したアイテムのぶんが
            // 画像1枚ぶんずつ残り続ける。
            textureBridge.RetainOnly(aliveItems.Cast<object>().ToHashSet());
        }

        public void Dispose()
        {
            pipeline.Dispose();
            textureBridge.Dispose();
        }

        private static Matrix4x4 BuildSizeMatrix(I3DProvider provider, RawRectF? imageBounds)
        {
            // プロバイダーが実寸を知っている場合はそちらを優先する。
            if (provider is I3DSizeProvider sizeProvider)
            {
                // 実寸を自分で扱うプロバイダーには掛けない。掛けると出力経路と
                // 大きさが食い違う（点群3D が画像の大きさぶん余計に広がっていた）。
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
