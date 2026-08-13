using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM43D.Flat3D
{
    /// <summary>
    /// アイテムの絵をそのまま 3D 空間の板として置きます。
    /// </summary>
    /// <remarks>
    /// 3D の図形やエフェクトは、もともとシーンカメラの視点で出力されます。ところが
    /// ふつうのテキストや画像は YMM4 が 2D のまま描くので、カメラを動かしても
    /// 動きません。このエフェクトを付けたアイテムだけが、他の 3D 物体と同じように
    /// カメラに従うようになります。
    /// <para>
    /// 設定はありません。カメラが既定の位置にあるときは、付けていないときと同じ
    /// 大きさ・同じ場所に写ります。
    /// </para>
    /// </remarks>
    [VideoEffect("3D空間に置く", ["3D"], [])]
    public class Flat3DEffect : VideoEffect3DBase
    {
        public override string Label => "3D空間に置く";

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Flat3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
