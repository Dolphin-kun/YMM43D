using System.Numerics;
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
        /// <summary>
        /// YMM4 の座標がピクセル単位なのに対し、3D空間は 1 単位を 100px として扱う。
        /// </summary>
        private const float PixelsPerUnit = 100f;

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
            if (texture is null && needsImage && rendered.Image is { } image)
                texture = textureBridge.GetTexture(environment.Device, environment.Devices, image, item, out _);

            return new DrawContext3D
            {
                World = BuildWorldMatrix(item, itemTime, provider, rendered.CameraMatrix),
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
            Matrix4x4 itemCamera)
        {
            var sizeScale = BuildSizeMatrix(provider);
            var zoom = Matrix4x4.CreateScale(item.Zoom.GetFloat(time) / 100f);

            // YMM4 の回転は時計回り、3D空間は反時計回りなので符号を反転する。
            var rotation = Matrix4x4.CreateRotationZ(-Rotation3D.ToRadians(item.Rotation.GetFloat(time)));

            // YMM4 の Y 軸は下向き、3D空間は上向き。
            var translation = Matrix4x4.CreateTranslation(
                item.X.GetFloat(time) / PixelsPerUnit,
                -item.Y.GetFloat(time) / PixelsPerUnit,
                item.Z.GetFloat(time) / PixelsPerUnit);

            if (itemCamera == Matrix4x4.Identity)
                return sizeScale * zoom * rotation * translation;

            return sizeScale * zoom * rotation * ToYUpMatrix(itemCamera) * translation;
        }

        /// <summary>
        /// 描画対象の実寸を、3D空間での大きさと中心のずれに変換します。
        /// </summary>
        private static Matrix4x4 BuildSizeMatrix(I3DProvider provider)
        {
            if (provider is not I3DSizeProvider sizeProvider
                || !sizeProvider.TryGetSize(out var size, out var offset))
            {
                return Matrix4x4.Identity;
            }

            // トリミングされている場合、画像の中心はアイテムの原点からずれる。
            var center = offset + size / 2f;

            return Matrix4x4.CreateScale(size.X / PixelsPerUnit, size.Y / PixelsPerUnit, 1f)
                 * Matrix4x4.CreateTranslation(center.X / PixelsPerUnit, -center.Y / PixelsPerUnit, 0f);
        }

        /// <summary>
        /// YMM4 の Y 軸下向き・ピクセル単位の行列を、3D空間の Y 軸上向き・
        /// 単位系に変換します。
        /// </summary>
        private static Matrix4x4 ToYUpMatrix(Matrix4x4 matrix)
        {
            // Y 軸を反転する基底変換 S * M * S（S = diag(1, -1, 1, 1)）は、
            // Y が絡む成分の符号を入れ替えることと同じ。
            matrix.M12 = -matrix.M12;
            matrix.M21 = -matrix.M21;
            matrix.M23 = -matrix.M23;
            matrix.M32 = -matrix.M32;

            matrix.M41 /= PixelsPerUnit;
            matrix.M42 /= -PixelsPerUnit;
            matrix.M43 /= PixelsPerUnit;

            return matrix;
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
