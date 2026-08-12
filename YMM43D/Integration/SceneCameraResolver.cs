using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Integration
{
    /// <summary>
    /// いまシーンを撮っているカメラを決めます。
    /// </summary>
    /// <remarks>
    /// タイムラインに <see cref="ISceneCamera"/> なアイテムがあればそれを使い、
    /// 無ければ正面から見た既定のカメラを使います。
    /// </remarks>
    public static class SceneCameraResolver
    {
        /// <summary>その時刻に効いているカメラアイテム。</summary>
        public readonly record struct ActiveCamera(IItem Item, ISceneCamera Camera, FrameContext ItemTime);

        /// <summary>描画要求に対応するカメラの設定値を返します。</summary>
        public static CameraState Resolve(TimelineItemSourceDescription description)
            => Resolve(TimelineLookup.Find(description), description.TimelinePosition.Frame, description.FPS);

        /// <summary>タイムラインの現在位置におけるカメラの設定値を返します。</summary>
        public static CameraState Resolve(Timeline timeline)
            => Resolve(timeline, timeline.CurrentFrame, Math.Max(1, timeline.VideoInfo.FPS));

        private static CameraState Resolve(Timeline? timeline, int frame, int fps)
            => Find(timeline, frame, fps) is { } active
                ? active.Camera.GetState(active.ItemTime)
                : CameraState.Default;

        /// <summary>
        /// その時刻に効いているカメラアイテムを探します。
        /// </summary>
        /// <remarks>
        /// 重なっている場合はレイヤー番号がいちばん大きいものを使います。手前に
        /// 置いたカメラが勝つほうが、重ねて差し替えるときに分かりやすいためです。
        /// </remarks>
        public static ActiveCamera? Find(Timeline? timeline, int frame, int fps)
        {
            if (timeline?.Items is not { } items)
                return null;

            ActiveCamera? found = null;

            foreach (var candidate in items)
            {
                if (candidate.IsHidden || candidate is not ISceneCamera camera)
                    continue;

                if (frame < candidate.Frame || frame >= candidate.Frame + candidate.Length)
                    continue;

                if (found is { } current && candidate.Layer <= current.Item.Layer)
                    continue;

                // キーフレームはアイテムの長さに対して打たれる。タイムライン全体の
                // 位置で評価すると、アイテムを動かしただけで動きが変わってしまう。
                var itemTime = new FrameContext(
                    frame - candidate.Frame, Math.Max(1, candidate.Length), fps);

                found = new ActiveCamera(candidate, camera, itemTime);
            }

            return found;
        }

        /// <summary>タイムラインの現在位置で効いているカメラアイテムを探します。</summary>
        public static ActiveCamera? Find(Timeline timeline)
            => Find(timeline, timeline.CurrentFrame, Math.Max(1, timeline.VideoInfo.FPS));
    }
}
