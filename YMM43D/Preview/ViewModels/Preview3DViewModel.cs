using System.Collections.ObjectModel;
using System.ComponentModel;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Preview.ViewModels
{
    public class Preview3DViewModel : Bindable, ITimelineToolViewModel, IDisposable
    {
        #region Fields
        private Timeline? timeline;
        private TimelineToolInfo? toolInfo;
        private List<I3DProvider> lastProviders = [];
        private SceneCamera sceneCamera = SceneCamera.Instance;
        private SceneCamera freeCamera = new();
        private readonly CameraChangeTracker sceneCameraTracker = new();
        #endregion

        #region Properties
        public int CurrentFrame => timeline?.CurrentFrame ?? 0;
        public int FPS => timeline?.VideoInfo.FPS ?? 60;
        public ObservableCollection<PreviewItem> PreviewItems { get; } = [];
        public object? Scene => scene;
        public TimelineSourceDescription? TimelineSourceDescription => timelineSourceDescription;
        private object? scene;
        private TimelineSourceDescription? timelineSourceDescription;


        public SceneCamera SceneCamera => sceneCamera;
        public SceneCamera FreeCamera => freeCamera;
        #endregion

        public Preview3DViewModel()
        {
        }

        public void RefreshOutputPreviewIfCameraChanged()
        {
            if (timeline == null)
                return;

            int frame = timeline.CurrentFrame;
            int length = timeline.Length;
            int fps = timeline.VideoInfo.FPS;

            SceneCamera.UpdateTimelineContext(frame, length, fps);

            if (!sceneCameraTracker.HasChanged(SceneCamera, frame, length, fps))
                return;

            TouchShape3DParameters();
            ForceTimelineRefresh();
        }

        private void ForceTimelineRefresh()
        {
            if (timeline == null)
                return;

            int currentFrame = timeline.CurrentFrame;
            int alternateFrame = currentFrame < timeline.Length - 1
                ? currentFrame + 1
                : (currentFrame > 0 ? currentFrame - 1 : 0);

            if (alternateFrame == currentFrame)
            {
                UpdatePreviewTarget();
                return;
            }

            timeline.CurrentFrame = alternateFrame;
            timeline.CurrentFrame = currentFrame;
        }

        private void TouchShape3DParameters()
        {
            if (timeline?.Items == null)
                return;

            foreach (var item in timeline.Items.OfType<ShapeItem>())
            {
                if (item.ShapeParameter is ICameraSync param)
                {
                    param.TouchCameraSync();
                }
            }
        }

        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            timeline?.PropertyChanged -= OnTimelinePropertyChanged;

            toolInfo = info;
            timeline = info.Timeline;

            if (timeline != null)
            {
                UpdateTimelineSourceDescription();
                
                if (info.Scenes != null)
                {
                    scene = info.Scenes.AllScenes.FirstOrDefault(s => s.Timeline == timeline);
                }
                timeline.PropertyChanged += OnTimelinePropertyChanged;
                UpdatePreviewTarget();
            }
        }

        private void UpdateTimelineSourceDescription()
        {
            if (timeline == null || toolInfo == null) return;

            timelineSourceDescription = new TimelineSourceDescription(
                new System.Drawing.Size(timeline.VideoInfo.Width, timeline.VideoInfo.Height),
                new YukkuriMovieMaker.Player.Video.FrameTime(timeline.CurrentFrame, timeline.VideoInfo.FPS),
                new YukkuriMovieMaker.Player.Video.FrameTime(timeline.Length, timeline.VideoInfo.FPS),
                timeline.VideoInfo.FPS,
                TimelineSourceUsage.Playing,
                timeline.ID,
                toolInfo.Scenes?.AllScenes?.Cast<ISceneInfo>() ?? []
            );
        }

        private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName == nameof(Timeline.CurrentFrame) ||
                e.PropertyName == "Items" ||
                e.PropertyName == nameof(Timeline.VideoInfo))
            {
                if (e.PropertyName == nameof(Timeline.CurrentFrame))
                {
                    UpdateTimelineSourceDescription();
                    OnPropertyChanged(nameof(CurrentFrame));
                }
                
                UpdatePreviewTarget();
            }
        }

        public void UpdatePreviewTarget()
        {
            if (timeline == null) return;

            var frame = timeline.CurrentFrame;
            var items = timeline.Items;
            if (items == null) return;

            var activeVideoItems = items
                                   .Where(item => item != null && frame >= item.Frame && frame < (item.Frame + item.Length))
                                   .OfType<IVideoItem>()
                                   .ToArray();

            var currentProviders = new List<(I3DProvider Provider, IVideoItem Item)>();
            foreach (var item in activeVideoItems)
            {
                foreach (var p in GetProvidersFromVideoItem(item))
                {
                    currentProviders.Add((p, item));
                }
            }

            var newProviderList = currentProviders.Select(x => x.Provider).ToList();
            if (!newProviderList.SequenceEqual(lastProviders))
            {
                PreviewItems.Clear();
                foreach (var (p, item) in currentProviders)
                {
                    PreviewItems.Add(new PreviewItem(p, item, item.Frame, item.Length));
                }
                lastProviders = newProviderList;
            }
        }

        private readonly StandardVideoItemProvider defaultProvider = new();
        private bool isDisposed;

        private static I3DProvider? GetProviderFromShapeParameter(IVideoItem item)
        {
            if (item is ShapeItem shapeItem)
            {
                return ProviderRegistry.GetProvider(shapeItem.ShapeParameter);
            }

            return null;
        }

        private IEnumerable<I3DProvider> GetProvidersFromVideoItem(IVideoItem item)
        {
            var results = new List<I3DProvider>();
            if (item == null) return results;

            // 1. アイテム本体が I3DProvider を実装している場合
            if (item is I3DProvider selfProvider)
                results.Add(selfProvider);

            // 2. ShapeParameter に紐づいたプロバイダーを取得
            var shapeProvider = GetProviderFromShapeParameter(item);
            if (shapeProvider != null)
                results.Add(shapeProvider);

            // 3. エフェクトをチェック (標準プロパティアクセス)
            if (item.VideoEffects != null)
            {
                foreach (var effect in item.VideoEffects)
                {
                    if (effect is I3DProvider effectProvider)
                        results.Add(effectProvider);
                }
            }

            if (results.Count > 1)
                results = [.. results.Distinct()];

            // 5. 何も見つからなかった場合は、通常の VideoItem として表示するためのデフォルトプロバイダーを返す
            if (results.Count == 0)
                results.Add(defaultProvider);

            return results;
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            timeline?.PropertyChanged -= OnTimelinePropertyChanged;
            timeline = null;
            toolInfo = null;
            defaultProvider.Dispose();
            lastProviders.Clear();
        }
    }
}
