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
    /// <summary>
    /// 3Dプレビューで1つのアイテムを描画するための情報を組み立てます。
    /// </summary>
    /// <remarks>
    /// アイテムの位置・拡大率・回転・不透明度からワールド行列を作り、
    /// 必要なら 2D 描画結果をテクスチャ化します。
    /// </remarks>
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
                World = BuildWorldMatrix(item, itemTime, provider, rendered.CameraMatrix, imageBounds),
                Opacity = Math.Clamp(GetOpacity(item, itemTime), 0f, 1f),
                Blend = ToBlendMode(item.Blend),
                IsAlwaysOnTop = item.IsAlwaysOnTop,
                Time = itemTime,
                Texture = texture,
            };
        }

        /// <summary>
        /// 表示対象でなくなったアイテムの資源を解放します。
        /// </summary>
        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems) => pipeline.RetainOnly(aliveItems);

        public void Dispose()
        {
            pipeline.Dispose();
            textureBridge.Dispose();
        }

        /// <summary>
        /// 不透明度に、フェードイン・フェードアウトの効果を掛け合わせます。
        /// </summary>
        private static float GetOpacity(IVideoItem item, in FrameContext time)
        {
            var opacity = item.Opacity.GetFloat(time) / 100f;

            var fadeInFrames = item.FadeIn * time.Fps;
            if (fadeInFrames > 0 && time.Frame < fadeInFrames)
                opacity *= (float)(time.Frame / fadeInFrames);

            var fadeOutFrames = item.FadeOut * time.Fps;
            if (fadeOutFrames > 0 && time.Frame > time.Length - fadeOutFrames)
                opacity *= (float)((time.Length - time.Frame) / fadeOutFrames);

            return opacity;
        }

        private static Matrix4x4 BuildWorldMatrix(
            IVideoItem item,
            in FrameContext time,
            I3DProvider provider,
            Matrix4x4 itemCamera,
            RawRectF? imageBounds)
        {
            var sizeScale = BuildSizeMatrix(provider, imageBounds);
            var zoom = Matrix4x4.CreateScale(item.Zoom.GetFloat(time) / 100f);

            // YMM4 の回転は時計回り、3D空間は反時計回りなので符号を反転する。
            var rotation = Matrix4x4.CreateRotationZ(-Rotation3D.ToRadians(item.Rotation.GetFloat(time)));

            // YMM4 の Y 軸は下向き、3D空間は上向き。
            var translation = Matrix4x4.CreateTranslation(
                WorldScale.ToWorld(item.X.GetFloat(time)),
                -WorldScale.ToWorld(item.Y.GetFloat(time)),
                WorldScale.ToWorld(item.Z.GetFloat(time)));

            if (itemCamera == Matrix4x4.Identity)
                return sizeScale * zoom * rotation * translation;

            return sizeScale * zoom * rotation * WorldScale.ToYUpMatrix(itemCamera) * translation;
        }

        /// <summary>
        /// 描画対象の実寸を、3D空間での大きさと中心のずれに変換します。
        /// </summary>
        /// <param name="imageBounds">
        /// テクスチャ化したアイテム画像の描画範囲。プロバイダーが実寸を答えられない
        /// 場合に、板をこの範囲に合わせます。これが無いとすべてのアイテムが
        /// 1単位（100px）四方で描かれてしまいます。
        /// </param>
        private static Matrix4x4 BuildSizeMatrix(I3DProvider provider, RawRectF? imageBounds)
        {
            // プロバイダーが実寸を知っている場合はそちらを優先する。
            if (provider is I3DSizeProvider sizeProvider
                && sizeProvider.TryGetSize(out var size, out var offset))
            {
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

    /// <summary>
    /// 3Dプレビューが 1 フレーム描くのに必要な、シーンとデバイスの情報。
    /// </summary>
    internal readonly record struct PreviewEnvironment(
        ID3D11Device Device,
        IGraphicsDevicesAndContext Devices,
        Scene? Scene,
        TimelineSourceDescription? SourceDescription);
}
