using System.Collections.Concurrent;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Integration
{
    /// <summary>
    /// カメラが動いたときに、YMM4 の標準プレビューを描き直させます。
    /// </summary>
    /// <remarks>
    /// YMM4 はアイテムのパラメータが変わらない限り描画結果を使い回すため、カメラだけを
    /// 動かしても標準プレビューは更新されません。そこで
    /// <list type="number">
    /// <item>カメラ連動アイテムのダミーパラメータを更新して結果を無効化し、</item>
    /// <item>現在フレームを一度ずらして戻すことで再描画を促す</item>
    /// </list>
    /// という2段階の手当てをしています。どちらも再描画を依頼する公式な手段が
    /// 無いための回避策です。
    /// <para>
    /// 2段目はシーンを丸ごと描き直させるので<b>高くつきます</b>。スライダーを
    /// 動かしている間そのまま流すと、1回の操作で何十回も描き直しが走って詰まります。
    /// <see cref="MinIntervalMs"/> の間隔まで間引き、最後の1回は必ず届くようにしてあります。
    /// </para>
    /// </remarks>
    public sealed class TimelineRefresher
    {
        /// <summary>
        /// 描き直しを促す間隔の下限（ミリ秒）。
        /// </summary>
        /// <remarks>
        /// 短くすると追従は良くなりますが、シーン全体の描き直しが詰まって
        /// かえって遅れます。取りこぼしても次の呼び出しで届くので、
        /// 操作を終えたときの位置がずれることはありません。
        /// </remarks>
        private const long MinIntervalMs = 120;

        private static readonly ConcurrentDictionary<Guid, TimelineRefresher> shared = new();

        private readonly CameraChangeTracker tracker = new();

        private long lastRefreshAt;
        private int lastKnownFrame = -1;
        private bool isPending;

        /// <summary>
        /// タイムラインに対応する共有の <see cref="TimelineRefresher"/> を返します。
        /// </summary>
        /// <remarks>
        /// 3Dプレビューと3Dプレビュー設定は同時に開けます。別々に持たせると、
        /// 1回のカメラ操作に対して両方が描き直しを促し、費用が倍になります。
        /// </remarks>
        public static TimelineRefresher For(Timeline timeline) => shared.GetOrAdd(timeline.ID, _ => new TimelineRefresher());

        /// <summary>
        /// カメラが前回から変化していれば再描画を促します。
        /// </summary>
        /// <remarks>
        /// 間引きに掛かった場合でも変化は覚えているので、続けて呼べば必ず届きます。
        /// 呼び出し側は、値が変わったかどうかを気にせず定期的に呼んでください。
        /// </remarks>
        /// <returns>実際に再描画を促した場合は <c>true</c>。</returns>
        public bool RefreshIfCameraChanged(Timeline timeline, SceneCamera fallback)
        {
            // カメラアイテムを置いていればその値、無ければ既定カメラの値を見る。
            // どちらで動かしても描き直しが要るのは同じ。
            if (tracker.HasChanged(SceneCameraResolver.Resolve(timeline, fallback)))
                isPending = true;

            if (!isPending)
                return false;

            // YMM4 が自分でフレームを進めているなら再生中。放っておいても
            // 描き直されるので、フレームを揺らして邪魔をしない。
            var frame = timeline.CurrentFrame;
            var isAdvancing = lastKnownFrame >= 0 && lastKnownFrame != frame;
            lastKnownFrame = frame;

            if (isAdvancing)
            {
                isPending = false;
                NotifyCameraSync(timeline);
                return true;
            }

            var now = Environment.TickCount64;
            if (now - lastRefreshAt < MinIntervalMs)
                return false;

            lastRefreshAt = now;
            isPending = false;
            Refresh(timeline);
            lastKnownFrame = timeline.CurrentFrame;

            return true;
        }

        /// <summary>
        /// 変化判定を飛ばして即座に再描画を促します。
        /// ドラッグ操作などでカメラを直接書き換えた直後に使います。
        /// </summary>
        /// <remarks>
        /// これも間引きの対象です。ドラッグ中はマウスが動くたびに呼ばれるため、
        /// そのまま流すとシーンの描き直しが追いつきません。
        /// </remarks>
        public void ForceRefresh(Timeline timeline, SceneCamera fallback)
        {
            // 今の値を基準として記録しておかないと、次の
            // RefreshIfCameraChanged が同じ変化をもう一度拾ってしまう。
            tracker.Sync(SceneCameraResolver.Resolve(timeline, fallback));
            isPending = true;

            RefreshIfCameraChanged(timeline, fallback);
        }

        /// <summary>現在のタイムライン位置を表す <see cref="FrameContext"/>。</summary>
        public static FrameContext GetTime(Timeline timeline)
            => new(timeline.CurrentFrame, Math.Max(1, timeline.Length), Math.Max(1, timeline.VideoInfo.FPS));

        private static void Refresh(Timeline timeline)
        {
            NotifyCameraSync(timeline);
            NudgeCurrentFrame(timeline);
        }

        private static void NotifyCameraSync(Timeline timeline)
        {
            if (timeline.Items is null)
                return;

            foreach (var item in timeline.Items)
            {
                if (item is ShapeItem shape && shape.ShapeParameter is ICameraSync shapeSync)
                    shapeSync.TouchCameraSync();

                if (item is not IVideoItem videoItem || videoItem.VideoEffects is null)
                    continue;

                foreach (var effect in videoItem.VideoEffects)
                {
                    if (effect is ICameraSync effectSync)
                        effectSync.TouchCameraSync();
                }
            }
        }

        private static void NudgeCurrentFrame(Timeline timeline)
        {
            var current = timeline.CurrentFrame;
            var neighbour = current < timeline.Length - 1
                ? current + 1
                : current - 1;

            // 長さ1のタイムラインでは動かす先が無い。
            if (neighbour < 0)
                return;

            timeline.CurrentFrame = neighbour;
            timeline.CurrentFrame = current;
        }
    }
}
