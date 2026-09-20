using YMM43D.Commons;
using YMM43D.Player;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
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

        // 受け取る 3D の中身。同じグループ制御に前の 3D エフェクトがあればその結果、
        // 無ければ中のアイテムが持っている 3D（最後の 3D エフェクト、無ければ 3D の図形）。
        public static I3DProvider? Find(
            IVideoItem owner,
            EffectDescription description,
            IVideoEffect effect,
            IGraphicsDevicesAndContext? devices,
            I3DProvider self)
        {
            if (FindPreceding(description, effect) is { } preceding
                && InstanceAt(preceding, description, devices, self) is { } chained)
            {
                return chained;
            }

            return Find(owner, description.InputIndex, devices, self);
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

        // 同じグループ制御の中で、そのエフェクトの 1 つ前に置かれた 3D エフェクト。
        private static VideoEffect3DBase? FindPreceding(EffectDescription description, IVideoEffect effect)
        {
            if (FindGroup(description, effect) is not { } group)
                return null;

            VideoEffect3DBase? found = null;

            foreach (var candidate in group.VideoEffects ?? [])
            {
                if (ReferenceEquals(candidate, effect))
                    return found;

                if (candidate.IsEnabled && candidate is VideoEffect3DBase solid)
                    found = solid;
            }

            return null;
        }

        private static GroupItem? FindGroup(EffectDescription description, IVideoEffect effect)
        {
            foreach (var item in TimelineLookup.Find(description)?.Items ?? [])
            {
                if (item is not GroupItem group)
                    continue;

                foreach (var candidate in group.VideoEffects ?? [])
                {
                    if (ReferenceEquals(candidate, effect))
                        return group;
                }
            }

            return null;
        }

        // 前のエフェクトが、この中のアイテムのために作った処理側。
        private static I3DProvider? InstanceAt(
            VideoEffect3DBase preceding,
            EffectDescription description,
            IGraphicsDevicesAndContext? devices,
            I3DProvider self)
        {
            var instances = preceding.GetInstancesAt(description.Layer, devices);

            if (instances.Count == 0)
                return null;

            var found = description.InputIndex >= 0 && description.InputIndex < instances.Count
                ? instances[description.InputIndex]
                : instances[^1];

            return ReferenceEquals(found, self) ? null : found;
        }
    }
}
