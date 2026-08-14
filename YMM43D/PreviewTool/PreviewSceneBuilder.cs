using YMM43D.Commons;
using YMM43D.Player;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class PreviewSceneBuilder(I3DProvider fallbackProvider)
    {
        private readonly I3DProvider fallbackProvider = fallbackProvider;

        private (int Width, int Height, int Fps, int Frame, int Length) lastSignature;

        public TimelineSourceDescription? SourceDescription { get; private set; }

        public IReadOnlyList<PreviewItem> Items { get; private set; } = [];

        public void UpdateSource(Timeline timeline, TimelineToolInfo toolInfo)
        {
            var info = timeline.VideoInfo;
            var signature = (info.Width, info.Height, info.FPS, timeline.CurrentFrame, timeline.Length);

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

        public void UpdateItems(Timeline timeline)
        {
            if (timeline.Items is not { } items)
                return;

            var frame = timeline.CurrentFrame;
            var updated = new List<PreviewItem>();

            var visible = items
                .OfType<IVideoItem>()
                .Where(item => !IsComposite(item))
                .Where(item => LayerVisibility.IsShown(timeline, item))
                .Where(item => frame >= item.Frame && frame < item.Frame + item.Length)
                .OrderBy(item => item.Layer);

            foreach (var item in visible)
            {
                foreach (var provider in FindProviders(item))
                    updated.Add(new PreviewItem(provider, item, item.Frame, item.Length));
            }

            Items = updated;
        }

        public void Clear() => Items = [];

        // 下のレイヤーをまとめて1枚の絵にするアイテムは、3Dプレビューでは扱わない。
        // 1つずつ並べて描く作りと二重になるうえ、その絵は YMM4 が組み替えるもので、
        // こちらから作らせると組み替えの最中の絵に触ってプロセスごと落ちる。
        private static bool IsComposite(IVideoItem item)
            => item is EffectItem or GroupItem or FrameBufferItem or TransitionItem;

        private IEnumerable<I3DProvider> FindProviders(IVideoItem item)
        {
            var providers = new List<I3DProvider>();

            if (item is I3DProvider itemProvider)
                providers.Add(itemProvider);

            if (item is ShapeItem shape && Provider3DRegistry.Find(shape.ShapeParameter) is { } shapeProvider)
                providers.Add(shapeProvider);

            if (providers.Count > 0)
                return providers.Distinct();

            foreach (var effect in item.VideoEffects ?? [])
            {
                if (effect.IsEnabled && effect is I3DProvider effectProvider)
                    providers.Add(effectProvider);
            }

            return providers.Count == 0 ? [fallbackProvider] : providers.Distinct();
        }
    }
}
