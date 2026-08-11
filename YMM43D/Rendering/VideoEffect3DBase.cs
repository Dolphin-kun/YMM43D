using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM43D.Rendering
{
    /// <summary>
    /// 3D対応のビデオエフェクト（I3DVideoEffect）を実装するための、Core側の抽象共通基底クラスです。
    /// 各種ボイラープレート処理（カメラ同期やI3DProviderへの処理移譲など）をCoreで一元化します。
    /// </summary>
    public abstract class VideoEffect3DBase : VideoEffectBase, I3DVideoEffect, ICameraSync, I3DSizeProvider
    {
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public Animation CameraSync { get; } = new Animation(0, -1000000, 1000000);

        private int cameraSyncVersion;

        public virtual bool RequiresMappedTexture => false;

        /// <summary>
        /// このエフェクトに関連付けられている最新の3D描画プロバイダ（通常は対応するProcessor）です。
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public I3DProvider? LastProvider { get; set; }

        public void TouchCameraSync()
        {
            cameraSyncVersion = (cameraSyncVersion + 1) % 1000000;
            CameraSync.CopyFrom(new Animation(cameraSyncVersion, -1000000, 1000000));
            OnPropertyChanged(nameof(CameraSync));
        }

        protected virtual IEnumerable<IAnimatable> GetBaseAnimatables()
        {
            return [CameraSync];
        }

        public virtual void Draw(ID3D11Device device, ID3D11DeviceContext context, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            LastProvider?.Draw(device, context, view, projection, drawContext);
        }

        public virtual ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            if (LastProvider is I3DTextureProvider textureProvider)
            {
                return textureProvider.GetTexture(device);
            }
            return null;
        }

        public virtual bool TryGetSize(out float width, out float height, out Vector2 offset)
        {
            if (LastProvider is I3DSizeProvider sizeProvider)
            {
                return sizeProvider.TryGetSize(out width, out height, out offset);
            }
            width = 100f;
            height = 100f;
            offset = Vector2.Zero;
            return false;
        }
    }
}
