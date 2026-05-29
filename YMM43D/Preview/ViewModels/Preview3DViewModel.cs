using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Preview.Views;
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
        private readonly DisposeCollector disposer = new();
        private readonly CameraChangeTracker sceneCameraTracker = new();
        private readonly StandardVideoItemProvider defaultProvider = new();
        private readonly Preview3DRenderer renderer = new();

        private Timeline? timeline;
        private TimelineToolInfo? toolInfo;
        private List<I3DProvider> lastProviders = [];
        private SceneCamera sceneCamera = SceneCamera.Instance;
        private SceneCamera freeCamera = new();
        private D3D11Host? d3dHost;
        private object? scene;
        private TimelineSourceDescription? timelineSourceDescription;
        private bool isDisposed;

        public int CurrentFrame => timeline?.CurrentFrame ?? 0;
        public int FPS => timeline?.VideoInfo.FPS ?? 60;
        public ObservableCollection<PreviewItem> PreviewItems { get; } = [];
        public object? Scene => scene;
        public TimelineSourceDescription? TimelineSourceDescription => timelineSourceDescription;
        public SceneCamera SceneCamera => sceneCamera;
        public SceneCamera FreeCamera => freeCamera;

        public D3D11Host? D3DHost
        {
            get => d3dHost;
            private set => Set(ref d3dHost, value, nameof(D3DHost));
        }

        public Preview3DViewModel()
        {
            disposer.Collect(defaultProvider);
            disposer.Collect(renderer);
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

        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            disposer.RemoveAndDisposeAction(this);
            disposer.RemoveAndDisposeAction(timeline);
            disposer.RemoveAndDisposeAction(d3dHost);
            disposer.RemoveAndDispose(ref d3dHost);
            D3DHost = null;

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
                disposer.CollectAction(timeline, delegate
                {
                    timeline.PropertyChanged -= OnTimelinePropertyChanged;
                });

                var host = new D3D11Host();
                D3DHost = host;
                disposer.Collect(host);

                host.Render += OnRenderTarget;
                host.MouseAction += OnMouseAction;
                disposer.CollectAction(host, delegate
                {
                    host.Render -= OnRenderTarget;
                    host.MouseAction -= OnMouseAction;
                });

                CompositionTarget.Rendering += OnRendering;
                disposer.CollectAction(this, delegate
                {
                    CompositionTarget.Rendering -= OnRendering;
                });
                
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

            if (item is I3DProvider selfProvider)
                results.Add(selfProvider);

            var shapeProvider = GetProviderFromShapeParameter(item);
            if (shapeProvider != null)
                results.Add(shapeProvider);

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

            if (results.Count == 0)
                results.Add(defaultProvider);

            return results;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (d3dHost == null) return;

            RefreshOutputPreviewIfCameraChanged();
            UpdatePreviewTarget();

            d3dHost.RenderFrame();
        }

        private void OnRenderTarget(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            if (d3dHost == null) return;
            renderer.Draw(device, context, width, height, d3dHost, this);
        }

        private void OnMouseAction(Point pos, D3D11Host.MouseEventKind kind, int delta)
        {
            renderer.OnMouseAction(pos, kind, delta, this);
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            disposer.Dispose();
            timeline = null;
            toolInfo = null;
            lastProviders.Clear();
        }
    }
}
