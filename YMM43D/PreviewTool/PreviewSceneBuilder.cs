using System.Collections.Immutable;
using YMM43D.Commons;
using YMM43D.Player;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class PreviewSceneBuilder(I3DProvider fallbackProvider)
    {
        private readonly record struct Placed(I3DProvider Provider, ImmutableList<IVideoEffect> Effects);

        private readonly I3DProvider fallbackProvider = fallbackProvider;

        private (Guid Timeline, int Width, int Height, int Fps, int Frame, int Length) lastSignature;

        public TimelineSourceDescription? SourceDescription { get; private set; }

        public IReadOnlyList<PreviewItem> Items { get; private set; } = [];

        public void UpdateSource(Timeline timeline, TimelineToolInfo toolInfo)
        {
            var info = timeline.VideoInfo;
            var signature = (timeline.ID, info.Width, info.Height, info.FPS, timeline.CurrentFrame, timeline.Length);

            if (SourceDescription is not null && signature == lastSignature)
                return;

            lastSignature = signature;
            SourceDescription = new TimelineSourceDescription(
                new System.Drawing.Size(info.Width, info.Height),
                new YukkuriMovieMaker.Player.Video.FrameTime(timeline.CurrentFrame, info.FPS),
                new YukkuriMovieMaker.Player.Video.FrameTime(timeline.Length, info.FPS),
                info.FPS,
                TimelineSourceUsage.Playing,
                timeline.ID,
                toolInfo.Scenes?.AllScenes?.Cast<ISceneInfo>() ?? []);
        }

        public void UpdateItems(Timeline timeline, IGraphicsDevicesAndContext? devices)
        {
            if (timeline.Items is not { } items)
                return;

            var frame = timeline.CurrentFrame;
            var fps = Math.Max(1, timeline.VideoInfo.FPS);
            var updated = new List<PreviewItem>();

            var flattening = new GroupFlattening(
                GroupLookup.Build(timeline, frame, fps), devices, SourceDescription, frame);

            var alive = items
                .OfType<IVideoItem>()
                .Where(item => LayerVisibility.IsShown(timeline, item))
                .Where(item => FrameContext.IsAlive(item, frame))
                .ToArray();

            var placedComposites = alive
                .Where(item => item is GroupItem { IsComposite: true } && SceneDepthCollector.HasSolidEffect(item))
                .ToArray();

            var visible = alive
                .Where(item => IsDrawn(item))
                .Where(item => !placedComposites.Any(composite => SceneDepthCollector.Composes(composite, item)))
                .OrderBy(item => item.Layer);

            foreach (var item in visible)
            {
                foreach (var (provider, effects) in FindProviders(item, flattening, devices))
                    updated.Add(new PreviewItem(provider, effects, item));
            }

            Items = updated;
        }

        public void Clear() => Items = [];

        private static bool IsDrawn(IVideoItem item) => item switch
        {
            EffectItem or TransitionItem => false,
            GroupItem group => group.IsComposite && SceneDepthCollector.HasSolidEffect(group),
            FrameBufferItem => SceneDepthCollector.HasSolidEffect(item),
            _ => true,
        };

        private IEnumerable<Placed> FindProviders(
            IVideoItem item, GroupFlattening flattening, IGraphicsDevicesAndContext? devices)
        {
            var effects = item.VideoEffects ?? [];

            if (flattening.Flattens(item))
                return [new Placed(fallbackProvider, [.. Flattened(effects), .. flattening.EffectsFor(item)])];

            if (SceneDepthCollector.FindGroupSolids(item, flattening.Groups, devices) is { Count: > 0 } groupSolids)
            {
                ImmutableList<IVideoEffect> flat = [.. effects.Where(effect => effect.IsEnabled && effect is not I3DProvider)];

                return groupSolids.Select(provider => new Placed(provider, flat));
            }

            // 板として描くアイテムには、YMM4 と同じくアイテムのエフェクトの後にグループ制御のエフェクトを掛ける。
            // ここに来るのは、グループ制御が位置を変えず、3D エフェクトも付いていないときだけ。
            ImmutableList<IVideoEffect> flatWithGroups = [.. Flattened(effects), .. flattening.EffectsFor(item)];

            if (!SceneDepthCollector.HasSolidEffect(item))
            {
                var sources = SceneDepthCollector.FindSources(item, devices).ToArray();

                return sources.Length > 0
                    ? sources.Select(provider => new Placed(provider, []))
                    : [new Placed(fallbackProvider, flatWithGroups)];
            }

            var solids = new List<Placed>();

            foreach (var effect in effects)
            {
                if (effect.IsEnabled
                    && effect is I3DProvider provider
                    && SceneDepthCollector.IsPlacedIn3D(item, provider))
                {
                    var preceding = Preceding(effects, effect);

                    foreach (var instance in SceneDepthCollector.Instances(provider, devices))
                        solids.Add(new Placed(instance, preceding));
                }
            }

            return solids.Count > 0 ? solids : [new Placed(fallbackProvider, flatWithGroups)];
        }

        private static ImmutableList<IVideoEffect> Preceding(
            IEnumerable<IVideoEffect> effects, IVideoEffect solid)
        {
            var taken = new List<IVideoEffect>();

            foreach (var effect in effects)
            {
                if (ReferenceEquals(effect, solid))
                    break;

                if (effect.IsEnabled && effect is not I3DProvider)
                    taken.Add(effect);
            }

            return [.. taken];
        }

        private static ImmutableList<IVideoEffect> Flattened(IEnumerable<IVideoEffect> effects)
            => [.. effects.Where(effect => effect.IsEnabled)];
    }
}
