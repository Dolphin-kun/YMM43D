using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public sealed class GroupFlattening(
        GroupLookup groups,
        IGraphicsDevicesAndContext? devices,
        TimelineSourceDescription? source,
        int timelineFrame)
    {
        private readonly Dictionary<GroupItem, bool> verdicts = new(ReferenceEqualityComparer.Instance);

        public GroupLookup Groups { get; } = groups;

        public bool Flattens(IItem item)
        {
            if (devices is null || source is null || Groups.IsEmpty)
                return false;

            foreach (var group in Groups.GetGroups(item))
            {
                if (!verdicts.TryGetValue(group, out var moves))
                    moves = verdicts[group] = GroupEffectProbe.MovesPlacement(group, devices, source, timelineFrame);

                if (moves)
                    return true;
            }

            return false;
        }

        public IReadOnlyList<IVideoEffect> EffectsFor(IItem item)
            => [.. Groups.GetGroups(item).SelectMany(GroupEffectProbe.EnabledEffects)];
    }
}
