using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class LayerVisibility
    {
        public static bool IsShown(Timeline? timeline, IItem item)
        {
            if (item.IsHidden)
                return false;

            if (timeline?.LayerSettings is not { } layers || item.Layer < 0)
                return true;

            return layers.IsVisibles[item.Layer];
        }
    }
}
