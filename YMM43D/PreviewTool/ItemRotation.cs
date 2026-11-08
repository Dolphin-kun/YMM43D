using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal static class ItemRotation
    {
        public static ISceneRotation3D? Find(IVideoItem item)
        {
            if (item is ShapeItem shape && shape.ShapeParameter is ISceneRotation3D parameter)
                return parameter;

            var effects = item.VideoEffects ?? [];

            foreach (var effect in effects)
            {
                if (effect.IsEnabled && effect is ISceneRotation3D found)
                    return found;
            }

            return null;
        }
    }
}
