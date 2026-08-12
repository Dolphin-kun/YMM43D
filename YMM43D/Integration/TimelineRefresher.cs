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
    /// </remarks>
    public sealed class TimelineRefresher
    {
        private readonly CameraChangeTracker tracker = new();

        /// <summary>
        /// カメラが前回から変化していれば再描画を促します。
        /// </summary>
        /// <returns>再描画を促した場合は <c>true</c>。</returns>
        public bool RefreshIfCameraChanged(Timeline timeline, SceneCamera camera)
        {
            var time = GetTime(timeline);
            if (!tracker.HasChanged(camera, time))
                return false;

            Refresh(timeline);
            return true;
        }

        /// <summary>
        /// 変化判定を飛ばして即座に再描画を促します。
        /// ドラッグ操作などでカメラを直接書き換えた直後に使います。
        /// </summary>
        public void ForceRefresh(Timeline timeline, SceneCamera camera)
        {
            // 今の値を基準として記録しておかないと、次の
            // RefreshIfCameraChanged が同じ変化をもう一度拾ってしまう。
            tracker.Sync(camera, GetTime(timeline));
            Refresh(timeline);
        }

        /// <summary>現在のタイムライン位置を表す <see cref="FrameContext"/>。</summary>
        public static FrameContext GetTime(Timeline timeline)
            => new(timeline.CurrentFrame, Math.Max(1, timeline.Length), Math.Max(1, timeline.VideoInfo.FPS));

        private static void Refresh(Timeline timeline)
        {
            NotifyCameraSync(timeline);
            NudgeCurrentFrame(timeline);
        }

        /// <summary>
        /// カメラ連動を宣言しているアイテム・エフェクトに、カメラが変わったことを伝えます。
        /// </summary>
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

        /// <summary>
        /// 現在フレームを隣に動かしてすぐ戻すことで、YMM4 に再描画させます。
        /// </summary>
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
