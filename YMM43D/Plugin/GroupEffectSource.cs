using YMM43D.Commons;
using YMM43D.Player;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Plugin
{
    // グループ制御に付けたエフェクトは、中のアイテム 1 つずつに掛かる。
    // そのとき、アイテムが自分で持っている 3D の中身を受け取るための決まりごとをまとめる。
    public static class GroupEffectSource
    {
        // 持ち主のアイテム自身のエフェクトなら false。グループ制御に付けたものなら true。
        public static bool IsOnGroup(IVideoItem owner, IVideoEffect effect)
        {
            foreach (var candidate in owner.VideoEffects ?? [])
            {
                if (ReferenceEquals(candidate, effect))
                    return false;
            }

            return true;
        }

        // そのアイテムが持っている 3D の中身。最後の 3D エフェクト、無ければ 3D の図形。
        public static I3DProvider? Find(
            IVideoItem owner, int inputIndex, IGraphicsDevicesAndContext? devices, I3DProvider self)
        {
            I3DProvider? found = null;

            foreach (var candidate in owner.VideoEffects ?? [])
            {
                if (candidate.IsEnabled
                    && candidate is VideoEffect3DBase solid
                    && solid.GetInstance(inputIndex, devices) is { } instance
                    && !ReferenceEquals(instance, self))
                {
                    found = instance;
                }
            }

            return found ?? SceneDepthCollector
                .FindSources(owner, devices)
                .FirstOrDefault(source => !ReferenceEquals(source, self));
        }
    }
}
