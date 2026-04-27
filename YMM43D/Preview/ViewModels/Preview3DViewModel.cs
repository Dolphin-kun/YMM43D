using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Preview.ViewModels
{
    public class Preview3DViewModel : Bindable, ITimelineToolViewModel
    {
        #region Fields
        private Timeline? timeline;
        private TimelineToolInfo? toolInfo;
        private List<I3DProvider> lastProviders = [];
        private bool isSettingsVisible = false;

        private SceneCamera sceneCamera = SceneCamera.Instance;
        private SceneCamera freeCamera = new();
        private bool isLockToCamera = false;
        #endregion

        #region Properties
        public string Title => "3Dプレビュー";
        public int CurrentFrame => timeline?.CurrentFrame ?? 0;
        public int FPS => timeline?.VideoInfo.FPS ?? 60;
        public ObservableCollection<PreviewItem> PreviewItems { get; } = [];
        public object? Scene => scene;
        public TimelineSourceDescription? TimelineSourceDescription => timelineSourceDescription;
        private object? scene;
        private TimelineSourceDescription? timelineSourceDescription;

        public bool IsSettingsVisible
        {
            get => isSettingsVisible;
            set => Set(ref isSettingsVisible, value);
        }

        public SceneCamera SceneCamera => sceneCamera;
        public SceneCamera FreeCamera => freeCamera;

        public bool IsLockToCamera
        {
            get => isLockToCamera;
            set
            {
                if (Set(ref isLockToCamera, value))
                {
                    OnPropertyChanged(nameof(ActiveCamera));
                }
            }
        }

        public SceneCamera ActiveCamera => IsLockToCamera ? SceneCamera : FreeCamera;

        public Preview3DSettingsViewModel SettingsViewModel { get; } = new();
        #endregion

        #region Commands
        public ICommand ToggleSettingsCommand { get; }
        public ICommand ResetCameraCommand { get; }
        #endregion

        public Preview3DViewModel()
        {
            ToggleSettingsCommand = new ActionCommand(
                _ => true,
                _ => IsSettingsVisible = !IsSettingsVisible
            );

            ResetCameraCommand = new ActionCommand(
                _ => true,
                _ => SceneCamera.Reset()
            );

            SettingsViewModel.Camera = SceneCamera;
        }

        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            if (timeline != null)
                timeline.PropertyChanged -= OnTimelinePropertyChanged;

            toolInfo = info;
            timeline = info.Timeline;
            
            if (timeline != null)
            {
                UpdateTimelineSourceDescription();
                
                // 現在のタイムラインに対応する Scene を取得
                if (info.Scenes != null)
                {
                    scene = info.Scenes.AllScenes.FirstOrDefault(s => s.Timeline == timeline);
                }
            }
            
            if (timeline != null)
            {
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

            // リフレクションを排除したため、毎フレーム実行しても十分に高速です
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

            // プロバイダーの一覧に変更があった場合のみ ObservableCollection を更新
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

        private static readonly StandardVideoItemProvider defaultProvider = new();

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
                results = results.Distinct().ToList();

            // 5. 何も見つからなかった場合は、通常の VideoItem として表示するためのデフォルトプロバイダーを返す
            if (results.Count == 0)
                results.Add(defaultProvider);

            return results;
        }
    }
}
