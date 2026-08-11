using System.ComponentModel;
using Vortice.Direct3D11;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D描画を行う映像エフェクトの基底クラス。
    /// </summary>
    /// <remarks>
    /// 派生クラスは <see cref="VideoEffectBase.CreateVideoEffect"/> で
    /// <see cref="I3DProvider"/> を実装したプロセッサを作り、
    /// <see cref="AttachProcessor"/> に渡してください。3Dプレビューからの描画要求は
    /// そのプロセッサに転送されます。
    /// <para>
    /// カメラ連動に必要なダミーパラメータは <see cref="CameraSyncAnimation"/> として
    /// 用意済みです。<c>GetAnimatables()</c> の戻り値に必ず含めてください。
    /// </para>
    /// </remarks>
    public abstract class VideoEffect3DBase
        : VideoEffectBase, I3DVideoEffect, ICameraSync, I3DSizeProvider, I3DLocalTransform
    {
        private readonly CameraSync cameraSync = new();

        protected VideoEffect3DBase()
        {
            cameraSync.Changed += () => OnPropertyChanged(nameof(CameraSyncAnimation));
        }

        /// <summary>
        /// カメラの変化を YMM4 に伝えるためだけのアニメーション。
        /// エディタには表示されませんが、<c>GetAnimatables()</c> には含める必要があります。
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public Animation CameraSyncAnimation => cameraSync.Value;

        /// <summary>
        /// このエフェクトに対応する、現在有効な 3D 描画プロセッサ。
        /// </summary>
        protected I3DProvider? Processor { get; private set; }

        /// <inheritdoc/>
        public virtual bool RequiresMappedTexture => false;

        public void TouchCameraSync() => cameraSync.TouchCameraSync();

        /// <summary>
        /// 3D描画を担うプロセッサを結び付けます。
        /// <see cref="VideoEffectBase.CreateVideoEffect"/> の中で呼んでください。
        /// </summary>
        protected TProcessor AttachProcessor<TProcessor>(TProcessor processor) where TProcessor : I3DProvider
        {
            Processor = processor;
            return processor;
        }

        /// <summary>
        /// プロセッサが破棄されたことを伝えます。
        /// 別のプロセッサが既に結び付いている場合は何もしません。
        /// </summary>
        public void DetachProcessor(I3DProvider processor)
        {
            if (ReferenceEquals(Processor, processor))
                Processor = null;
        }

        /// <summary>
        /// 既定では結び付いているプロセッサに描画を委譲します。
        /// </summary>
        public virtual void Draw(in Render3DContext render, DrawContext3D item)
            => Processor?.Draw(render, item);

        /// <inheritdoc/>
        public virtual ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
            => Processor is I3DTextureProvider provider ? provider.GetTexture(device) : null;

        /// <inheritdoc/>
        public virtual bool TryGetLocalMatrix(out System.Numerics.Matrix4x4 matrix)
        {
            if (Processor is I3DLocalTransform provider)
                return provider.TryGetLocalMatrix(out matrix);

            matrix = System.Numerics.Matrix4x4.Identity;
            return false;
        }

        /// <inheritdoc/>
        public virtual bool TryGetSize(out System.Numerics.Vector2 size, out System.Numerics.Vector2 offset)
        {
            if (Processor is I3DSizeProvider provider)
                return provider.TryGetSize(out size, out offset);

            size = default;
            offset = default;
            return false;
        }
    }
}
