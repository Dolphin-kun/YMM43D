using YukkuriMovieMaker.Commons;

namespace YMM43D.Camera
{
    /// <summary>
    /// カメラが動いたことを知らせる先。
    /// </summary>
    /// <remarks>
    /// YMM4 はアイテムのパラメータが変わらない限り描画結果を使い回します。
    /// カメラはアイテムのパラメータではないため、カメラだけを動かしても再描画されません。
    /// このインターフェースを実装したアイテムやエフェクトには、カメラが変わったときに
    /// <see cref="TouchCameraSync"/> が呼ばれ、再描画のきっかけを与えます。
    /// </remarks>
    public interface ICameraSync
    {
        void TouchCameraSync();
    }

    /// <summary>
    /// <see cref="ICameraSync"/> の標準実装。
    /// ダミーの <see cref="Animation"/> を持ち、その値を変えることで
    /// YMM4 にパラメータが変化したと認識させます。
    /// </summary>
    /// <remarks>
    /// このオブジェクトが公開する <see cref="Value"/> を、アイテム側の
    /// <c>GetAnimatables()</c> の戻り値に必ず含めてください。含めないと
    /// YMM4 が変化を検知せず、カメラを動かしても表示が更新されません。
    /// </remarks>
    public sealed class CameraSync : ICameraSync
    {
        private const double Min = -1000000;
        private const double Max = 1000000;

        private int version;

        /// <summary>
        /// YMM4 に変化を伝えるためだけのアニメーション。
        /// エディタに出さないよう、公開側で <c>[Browsable(false)]</c> を付けてください。
        /// </summary>
        public Animation Value { get; } = new(0, Min, Max);

        /// <summary>値が変化したときに発生します。</summary>
        public event Action? Changed;

        public void TouchCameraSync()
        {
            version = (version + 1) % (int)Max;
            Value.CopyFrom(new Animation(version, Min, Max));
            Changed?.Invoke();
        }
    }
}
