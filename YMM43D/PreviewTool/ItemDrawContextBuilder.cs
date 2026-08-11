using System.Numerics;
using Vortice.Direct2D1;
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

        private readonly Dictionary<IVideoItem, ISource> sources = [];
        private readonly D2DTextureBridge textureBridge = new();
        private readonly ItemTransformResolver transformResolver = new();
        private readonly Lock gate = new();

        public DrawContext3D Build(
            IVideoItem item,
            in FrameContext itemTime,
            PreviewEnvironment environment,
            I3DProvider provider)
        {
            var texture = provider.RequiresMappedTexture
                ? GetTexture(item, itemTime, environment, provider)
                : null;

            return new DrawContext3D
            {
                World = BuildWorldMatrix(item, itemTime, environment, provider),
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
        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems)
        {
            lock (gate)
            {
                foreach (var item in sources.Keys.Where(k => !aliveItems.Contains(k)).ToArray())
                {
                    if (sources.Remove(item, out var source))
                        source.Dispose();
                }
            }

            transformResolver.RetainOnly(aliveItems);
        }

        public void Clear()
        {
            lock (gate)
            {
                foreach (var source in sources.Values)
                    source.Dispose();
                sources.Clear();
            }

            textureBridge.Clear();
        }

        public void Dispose()
        {
            Clear();
            textureBridge.Dispose();
            transformResolver.Dispose();
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

        private Matrix4x4 BuildWorldMatrix(
            IVideoItem item,
            in FrameContext time,
            PreviewEnvironment environment,
            I3DProvider provider)
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

            var itemCamera = GetItemCameraMatrix(item, time, environment);
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

        private Matrix4x4 GetItemCameraMatrix(IVideoItem item, in FrameContext time, PreviewEnvironment environment)
        {
            if (environment.SourceDescription is not { } description)
                return Matrix4x4.Identity;

            var itemDescription = new TimelineItemSourceDescription(
                description, time.Frame, time.Length, item.Layer);

            return transformResolver.GetCameraMatrix(item, environment.Devices, itemDescription);
        }

        private ID3D11ShaderResourceView? GetTexture(
            IVideoItem item,
            in FrameContext time,
            PreviewEnvironment environment,
            I3DProvider provider)
        {
            // プロバイダーが自前のテクスチャを持っているならそちらを優先する。
            if (provider is I3DTextureProvider textureProvider
                && textureProvider.GetTexture(environment.Device) is { } ownTexture)
            {
                return ownTexture;
            }

            var image = GetItemImage(item, time, environment);
            if (image is null)
                return null;

            return textureBridge.GetTexture(environment.Device, environment.Devices, image, item, out _);
        }

        /// <summary>
        /// アイテムの 2D 描画結果を取得します。
        /// </summary>
        private ID2D1Image? GetItemImage(IVideoItem item, in FrameContext time, PreviewEnvironment environment)
        {
            if (environment.Scene is not { } scene || environment.SourceDescription is not { } description)
                return null;

            try
            {
                ISource? source;
                lock (gate)
                {
                    if (!sources.TryGetValue(item, out source))
                    {
                        source = item.CreateVideoSource(environment.Devices, scene);
                        if (source is null)
                            return null;
                        sources[item] = source;
                    }
                }

                source.Update(new TimelineItemSourceDescription(
                    description, time.Frame, time.Length, item.Layer));

                foreach (var output in source.Outputs ?? [])
                {
                    if (output?.Output is { } image)
                        return image;
                }
            }
            catch
            {
                // アイテムによっては描画元を作れないことがある。その場合は
                // テクスチャ無しで扱う。
            }

            return null;
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
