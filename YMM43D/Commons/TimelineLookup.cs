using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;

namespace YMM43D.Commons
{
    /// <summary>
    /// 描画要求から、いま描かれているシーンのタイムラインを探します。
    /// </summary>
    /// <remarks>
    /// <see cref="TimelineSourceDescription.Scenes"/> の要素は <c>ISceneInfo</c> として
    /// 渡されますが、実体は <see cref="Scene"/> でタイムラインを持っています。
    /// 型が違えば何も返しません。
    /// </remarks>
    public static class TimelineLookup
    {
        public static Timeline? Find(TimelineSourceDescription description)
        {
            foreach (var info in description.Scenes ?? [])
            {
                if (info is Scene scene && scene.ID == description.SceneId)
                    return scene.Timeline;
            }

            return null;
        }
    }
}
