using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// タイムラインから、いま 3D で描くものを組み立てます。
    /// </summary>
    /// <remarks>
    /// 「何を描くか」だけを受け持ちます。見る位置や掴む操作とは切り離してあるので、
    /// 描く対象の決め方を変えても操作側には影響しません。
    /// </remarks>
    internal sealed class PreviewSceneBuilder(I3DProvider fallbackProvider)
    {
        private readonly I3DProvider fallbackProvider = fallbackProvider;

        private (int Width, int Height, int Fps, int Frame, int Length) lastSignature;

        /// <summary>描画元の情報。まだ組み立てていなければ <c>null</c>。</summary>
        public TimelineSourceDescription? SourceDescription { get; private set; }

        /// <summary>いまの再生位置で描くもの。</summary>
        public IReadOnlyList<PreviewItem> Items { get; private set; } = [];

        /// <summary>
        /// 描画元の情報を、いまのタイムラインの設定に合わせます。
        /// </summary>
        /// <remarks>
        /// 画面の大きさや FPS は編集中に変えられます。作り直しは安くないので、
        /// 前回と違うときだけ組み立て直します。
        /// </remarks>
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

        /// <summary>いまの再生位置に出ているものを集め直します。</summary>
        public void UpdateItems(Timeline timeline)
        {
            if (timeline.Items is not { } items)
                return;

            var frame = timeline.CurrentFrame;
            var updated = new List<PreviewItem>();

            // 半透明な板は深度を書き込まないため、重なりは描画順で決まる。
            // YMM4 と同じく、番号の小さいレイヤーから先に描いて奥に置く。
            var visible = items
                .OfType<IVideoItem>()
                .Where(item => !item.IsHidden)
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

        /// <summary>
        /// そのアイテムを 3D で描くものを探します。
        /// </summary>
        /// <remarks>
        /// アイテム自身か図形が 3D を描けるなら、映像エフェクトは見ません。エフェクト側の
        /// 3D 描画はアイテムを平面化した絵をもとに立体を組み立てるものなので、既に立体で
        /// あるアイテムに重ねると本体と平たい写しの二重表示になります。
        /// </remarks>
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

            // 3D を描くものが無いアイテムは、2D の絵を板に貼って置く。
            return providers.Count == 0 ? [fallbackProvider] : providers.Distinct();
        }
    }
}
