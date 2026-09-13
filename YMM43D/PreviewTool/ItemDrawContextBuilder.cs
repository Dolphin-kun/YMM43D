using System.Collections.Immutable;
using System.Numerics;
using Vortice;
using YukkuriMovieMaker.Plugin.Effects;
using Vortice.Direct3D11;
using YMM43D.Player;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class ItemDrawContextBuilder : IDisposable
    {
        private readonly record struct Prepared(
            DrawDescription Draw, ID3D11ShaderResourceView? ImageTexture, RawRectF? ImageBounds);

        private readonly ItemRenderPipeline pipeline = new();
        private readonly D2DTextureBridge textureBridge = new();
        private readonly Dictionary<(IVideoItem Item, I3DProvider Provider), Prepared> prepared = [];

        public void Prepare(
            IVideoItem item,
            in FrameContext itemTime,
            PreviewEnvironment environment,
            I3DProvider provider,
            ImmutableList<IVideoEffect> effects)
        {
            var needsImage = provider.RequiresMappedTexture && GetProviderTexture(provider, environment) is null;

            var rendered = pipeline.Render(
                item, itemTime, environment, needsImage, provider, effects,
                ItemPlacement.ToDrawDescription(item, itemTime));

            ID3D11ShaderResourceView? texture = null;
            RawRectF? imageBounds = null;

            if (needsImage && rendered.Image is { } image)
            {
                texture = textureBridge.GetTexture(
                    environment.Device, environment.Devices, image, item, out var bounds);

                if (texture is not null)
                    imageBounds = bounds;
            }

            prepared[(item, provider)] = new Prepared(rendered.Draw, texture, imageBounds);
        }

        public DrawContext3D Build(
            IVideoItem item,
            in FrameContext itemTime,
            PreviewEnvironment environment,
            I3DProvider provider,
            in GroupLookup groups)
        {
            var providerTexture = GetProviderTexture(provider, environment);

            var ready = prepared.TryGetValue((item, provider), out var found)
                ? found
                : new Prepared(ItemPlacement.ToDrawDescription(item, itemTime), null, null);

            var texture = providerTexture ?? (provider.RequiresMappedTexture ? ready.ImageTexture : null);
            var imageBounds = providerTexture is null && texture is not null ? ready.ImageBounds : null;

            return new DrawContext3D
            {
                World = ItemPlacement.WithCamera(
                            BuildSizeMatrix(provider, imageBounds), ready.Draw.Camera)
                      * ItemPlacement.GetWorldMatrix(ready.Draw)
                      * groups.GetTransform(item),
                Opacity = Math.Clamp((float)ready.Draw.Opacity, 0f, 1f),
                Blend = ToBlendMode(item.Blend),
                IsAlwaysOnTop = item.IsAlwaysOnTop,
                Time = itemTime,
                Texture = texture,
            };
        }

        private static ID3D11ShaderResourceView? GetProviderTexture(I3DProvider provider, PreviewEnvironment environment)
            => provider is I3DTextureProvider textureProvider ? textureProvider.GetTexture(environment.Device) : null;

        public void Reset()
        {
            prepared.Clear();
            pipeline.Clear();
            textureBridge.Clear();
        }

        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems)
        {
            foreach (var key in prepared.Keys.Where(key => !aliveItems.Contains(key.Item)).ToArray())
                prepared.Remove(key);

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
