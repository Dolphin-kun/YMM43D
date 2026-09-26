using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Tachie;

namespace YMM43D.Project.Tachie
{
    // 立ち絵アイテムごとの設定。いまは持たせるものが無い。
    public class Model3DItemParameter : TachieItemParameterBase
    {
        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
