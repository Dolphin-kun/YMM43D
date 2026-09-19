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

        private readonly record struct PartKey(IVideoItem Item, I3DProvider Provider, int Part);

        private readonly ItemRenderPipeline pipeline = new();
        private readonly D2DTextureBridge textureBridge = new();
        private readonly Dictionary<(IVideoItem Item, I3DProvider Provider), Prepared[]> prepared = [];

        public void Prepare(
            IVideoItem item,
            in FrameContext itemTime,
            PreviewEnvironment environment,
            I3DProvider provider,
            ImmutableList<IVideoEffect> effects)
        {
            var needsImage = provider.RequiresMappedTexture && GetProviderTexture(provider, environment) is null;

            // アイテムの絵をそのまま板に貼るときだけ、文字ごとに分割したテキストなどを
            // 一つずつ別の物として並べる。3D エフェクトのプロセッサは入力ごとに別に居るので、そちらに任せる。
            var rendered = pipeline.Render(
                item, itemTime, environment, needsImage, allParts: needsImage, provider, effects,
                ItemPlacement.ToDrawDescription(item, itemTime));

            pipeline.RetainParts(item, provider, rendered.Count);

            var parts = new Prepared[rendered.Count];

            for (var i = 0; i < rendered.Count; i++)
            {
                ID3D11ShaderResourceView? texture = null;
                RawRectF? imageBounds = null;

                if (needsImage && rendered[i].Image is { } image)
                {
                    texture = textureBridge.GetTexture(
                        environment.Device, environment.Devices, image, new PartKey(item, provider, i), out var bounds);

                    if (texture is not null)
                        imageBounds = bounds;
                }

                parts[i] = new Prepared(rendered[i].Draw, texture, imageBounds);
            }

            prepared[(item, provider)] = parts;
        }

        public IReadOnlyList<DrawContext3D> Build(
            IVideoItem item,
            in FrameContext itemTime,
            PreviewEnvironment environment,
            I3DProvider provider,
            in GroupLookup groups)
        {
            var providerTexture = GetProviderTexture(provider, environment);

            var parts = prepared.TryGetValue((item, provider), out var found) && found.Length > 0
                ? found
                : [new Prepared(ItemPlacement.ToDrawDescription(item, itemTime), null, null)];

            var contexts = new DrawContext3D[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                var ready = parts[i];
                var texture = providerTexture ?? (provider.RequiresMappedTexture ? ready.ImageTexture : null);
                var imageBounds = providerTexture is null && texture is not null ? ready.ImageBounds : null;

                var world = provider is I3DPlacedInstance placed
                    && placed.TryGetPlacement(out var own)
                    && provider is I3DLocalTransform local
                    && local.TryGetLocalMatrix(out var localMatrix)
                        ? localMatrix * own
                        : ItemPlacement.WithCamera(BuildSizeMatrix(provider, imageBounds), ready.Draw.Camera)
                          * ItemPlacement.GetWorldMatrix(ready.Draw);

                contexts[i] = new DrawContext3D
                {
                    World = world * groups.GetTransform(item),
                    Opacity = Math.Clamp((float)ready.Draw.Opacity, 0f, 1f),
                    Blend = ToBlendMode(item.Blend),
                    IsAlwaysOnTop = item.IsAlwaysOnTop,
                    Time = itemTime,
                    Texture = texture,
                };
            }

            return contexts;
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

            textureBridge.RetainOnly(prepared
                .SelectMany(pair => Enumerable.Range(0, pair.Value.Length)
                    .Select(part => (object)new PartKey(pair.Key.Item, pair.Key.Provider, part)))
                .ToHashSet());
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
