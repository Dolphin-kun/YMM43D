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
    /// タイムラインに <see cref="ISceneCameraSource"/> なアイテムがあればそれを使い、
    /// 無ければ <see cref="SceneCameraRegistry"/> の既定カメラを使います。カメラ
    /// アイテムを置いていないシーンは、今までどおり3Dプレビュー設定のカメラで動きます。
    /// </remarks>
    public static class SceneCameraResolver
    {
        /// <summary>描画要求に対応するカメラの設定値を返します。</summary>
        public static CameraState Resolve(TimelineItemSourceDescription description)
            => Resolve(
                TimelineLookup.Find(description),
                description.TimelinePosition.Frame,
                description.FPS,
                SceneCameraRegistry.Get(description.SceneId),
                FrameContext.FromTimeline(description));

        /// <summary>タイムラインの現在位置におけるカメラの設定値を返します。</summary>
        public static CameraState Resolve(Timeline timeline, SceneCamera fallback)
            => Resolve(
                timeline,
                timeline.CurrentFrame,
                Math.Max(1, timeline.VideoInfo.FPS),
                fallback,
                TimelineRefresher.GetTime(timeline));

        private static CameraState Resolve(
            Timeline? timeline,
            int frame,
            int fps,
            SceneCamera fallback,
            in FrameContext fallbackTime)
        {
            if (timeline?.Items is { } items && FindCameraItem(items, frame) is { } camera)
            {
                // キーフレームはアイテムの長さに対して打たれる。タイムライン全体の
                // 位置で評価すると、アイテムを動かしただけで動きが変わってしまう。
                var itemTime = new FrameContext(
                    frame - camera.Item.Frame, Math.Max(1, camera.Item.Length), fps);

                return camera.Source.GetCameraState(itemTime);
            }

            return fallback.GetState(fallbackTime);
        }

        /// <summary>
        /// その時刻に効いているカメラアイテムを探します。
        /// </summary>
        /// <remarks>
        /// 重なっている場合はレイヤー番号がいちばん大きいものを使います。手前に
        /// 置いたカメラが勝つほうが、重ねて差し替えるときに分かりやすいためです。
        /// </remarks>
        private static (IVideoItem Item, ISceneCameraSource Source)? FindCameraItem(
            IEnumerable<IItem> items, int frame)
        {
            (IVideoItem Item, ISceneCameraSource Source)? found = null;

            foreach (var candidate in items)
            {
                if (candidate is not IVideoItem item || item.IsHidden || !ItemPlacement.IsAliveAt(item, frame))
                    continue;

                if (item is not ShapeItem shape || shape.ShapeParameter is not ISceneCameraSource source)
                    continue;

                if (found is null || item.Layer > found.Value.Item.Layer)
                    found = (item, source);
            }

            return found;
        }
    }
}
