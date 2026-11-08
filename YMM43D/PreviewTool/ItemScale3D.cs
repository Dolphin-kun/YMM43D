using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal static class ItemScale3D
    {
        public static ISceneScale3D? Find(IVideoItem item)
        {
            if (item is ShapeItem shape && shape.ShapeParameter is ISceneScale3D parameter)
                return parameter;

            var effects = item.VideoEffects ?? [];

            foreach (var effect in effects)
            {
                if (effect.IsEnabled && effect is ISceneScale3D found)
                    return found;
            }

            return null;
        }
    }
}
