using YMM43D.Commons;
using YMM43D.Player;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Plugin
{
    public static class GroupEffectSource
    {
        public static bool IsOnGroup(IVideoItem owner, IVideoEffect effect) => !Holds(owner.VideoEffects, effect);

        public static I3DProvider? Find(IVideoItem owner, EffectDescription description, IVideoEffect effect, IGraphicsDevicesAndContext? devices, I3DProvider self)
        {
            if (FindGroup(description, effect) is { } group
                && LastBefore(group.VideoEffects, effect) is { } preceding
                && InstanceAt(preceding, description, devices, self) is { } chained)
            {
                return chained;
            }

            return Find(owner, description.InputIndex, devices, self);
        }

        public static I3DProvider? FindInChain(IVideoItem owner, EffectDescription description, IVideoEffect effect, IGraphicsDevicesAndContext? devices, I3DProvider self)
            => LastBefore(owner.VideoEffects, effect) is { } preceding ? InstanceAt(preceding, description, devices, self) : null;

        public static I3DProvider? Find(IVideoItem owner, int inputIndex, IGraphicsDevicesAndContext? devices, I3DProvider self)
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

            return found ?? FindOwnSolid(owner, devices, self);
        }

        public static I3DProvider? FindOwnSolid(IVideoItem owner, IGraphicsDevicesAndContext? devices, I3DProvider self)
            => SceneDepthCollector.FindSources(owner, devices).FirstOrDefault(source => !ReferenceEquals(source, self));

        private static VideoEffect3DBase? LastBefore(IEnumerable<IVideoEffect>? effects, IVideoEffect effect)
        {
            VideoEffect3DBase? found = null;

            foreach (var candidate in effects ?? [])
            {
                if (ReferenceEquals(candidate, effect))
                    return found;

                if (candidate.IsEnabled && candidate is VideoEffect3DBase solid)
                    found = solid;
            }

            return null;
        }

        private static bool Holds(IEnumerable<IVideoEffect>? effects, IVideoEffect effect)
        {
            foreach (var candidate in effects ?? [])
            {
                if (ReferenceEquals(candidate, effect))
                    return true;
            }

            return false;
        }

        private static GroupItem? FindGroup(EffectDescription description, IVideoEffect effect)
        {
            foreach (var item in TimelineLookup.Find(description)?.Items ?? [])
            {
                if (item is GroupItem group && Holds(group.VideoEffects, effect))
                    return group;
            }

            return null;
        }

        private static I3DProvider? InstanceAt(VideoEffect3DBase preceding, EffectDescription description, IGraphicsDevicesAndContext? devices, I3DProvider self)
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
