using System.ComponentModel;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D図形アイテムのパラメータの基底クラス。
    /// </summary>
    /// <remarks>
    /// <para>
    /// パラメータは「アイテムの設定値とエディタ上の見た目」だけを持ち、
    /// 描画は <see cref="Shape3DSourceBase"/> 側が受け持ちます。
    /// YMM4 の <see cref="IShapeParameter"/> は <see cref="IDisposable"/> ではないため、
    /// パラメータに GPU リソースを持たせると解放される機会がありません。
    /// </para>
    /// <para>
    /// カメラ連動に必要なダミーパラメータは <see cref="CameraSyncAnimation"/> として
    /// 用意済みです。<c>GetAnimatables()</c> の戻り値に必ず含めてください。
    /// </para>
    /// </remarks>
    public abstract class ShapeParameter3DBase : ShapeParameterBase, ICameraSync
    {
        private readonly CameraSync cameraSync = new();

        protected ShapeParameter3DBase(SharedDataStore? sharedData) : base(sharedData)
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

        public void TouchCameraSync() => cameraSync.TouchCameraSync();

        /// <summary>
        /// このパラメータに対応する 3D 描画元を生成します。
        /// </summary>
        protected abstract Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices);

        /// <summary>
        /// 3D描画元を生成し、3Dプレビューから引けるように登録します。
        /// </summary>
        public sealed override IShapeSource CreateShapeSource(IGraphicsDevicesAndContext devices)
        {
            var source = Create3DSource(devices);
            Provider3DRegistry.Register(this, source);
            return source;
        }
    }
}
