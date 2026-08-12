using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Integration;
using YMM43D.Plugin;
using YMM43D.PreviewTool.Views;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.ItemEditor;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool.ViewModels
{
    public class Preview3DViewModel : Bindable, ITimelineToolViewModel, IDisposable
    {
        private readonly DisposeCollector disposer = new();
        private readonly Preview3DRenderer renderer = new();
        private readonly FreeCameraController freeCamera = new();

        private TimelineRefresher? refresher;
        private Timeline? timeline;
        private TimelineToolInfo? toolInfo;
        private SceneCamera sceneCamera = new();
        private D3D11Host? d3dHost;
        private Scene? scene;
        private TimelineSourceAndDevices? sourceAndDevices;
        private TimelineSourceDescription? sourceDescription;
        private List<PreviewItem> previewItems = [];
        private bool isDisposed;

        public SceneCamera SceneCamera
        {
            get => sceneCamera;
            private set
            {
                if (ReferenceEquals(sceneCamera, value))
                    return;

                sceneCamera.PropertyChanged -= OnSceneCameraPropertyChanged;
                Set(ref sceneCamera, value, nameof(SceneCamera));
                sceneCamera.PropertyChanged += OnSceneCameraPropertyChanged;
            }
        }

        public D3D11Host? D3DHost
        {
            get => d3dHost;
            private set => Set(ref d3dHost, value, nameof(D3DHost));
        }

        public ICommand ResetToSceneCameraCommand { get; }

        public Preview3DViewModel()
        {
            disposer.Collect(renderer);

            ResetToSceneCameraCommand = new ActionCommand(_ => true, _ => ResetToSceneCamera());

            sceneCamera.PropertyChanged += OnSceneCameraPropertyChanged;
            disposer.CollectAction(this, () => sceneCamera.PropertyChanged -= OnSceneCameraPropertyChanged);
        }

        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            disposer.RemoveAndDisposeAction(this);
            disposer.RemoveAndDisposeAction(timeline);
            disposer.RemoveAndDisposeAction(d3dHost);
            disposer.RemoveAndDispose(ref d3dHost);
            disposer.RemoveAndDispose(ref sourceAndDevices);
            D3DHost = null;

            toolInfo = info;
            timeline = info.Timeline;
            if (timeline is null)
                return;

            scene = info.Scenes?.AllScenes.FirstOrDefault(s => s.Timeline == timeline);
            SceneCamera = SceneCameraRegistry.Get(timeline.ID);
            refresher = TimelineRefresher.For(timeline);
            UpdateSourceDescription();

            if (scene is not null)
            {
                try
                {
                    sourceAndDevices = new TimelineSourceAndDevices(scene);
                    disposer.Collect(sourceAndDevices);
                }
                catch (Exception)
                {
                    // 描画元を用意できない場合でも、カメラ操作だけは行えるようにする。
                    sourceAndDevices = null;
                }
            }

            timeline.PropertyChanged += OnTimelinePropertyChanged;
            disposer.CollectAction(timeline, () => timeline.PropertyChanged -= OnTimelinePropertyChanged);

            var host = new D3D11Host();
            D3DHost = host;
            disposer.Collect(host);

            host.Render += OnRender;
            host.MouseAction += OnMouseAction;
            disposer.CollectAction(host, () =>
            {
                host.Render -= OnRender;
                host.MouseAction -= OnMouseAction;
            });

            CompositionTarget.Rendering += OnCompositionRendering;
            disposer.CollectAction(this, () => CompositionTarget.Rendering -= OnCompositionRendering);

            UpdatePreviewItems();
        }

        public void ResetToSceneCamera()
        {
            if (timeline is null)
                return;

            freeCamera.Invalidate();
            freeCamera.EnsureInitialized(ResolveCamera());
        }

        /// <summary>
        /// いまシーンを撮っているカメラ。カメラアイテムがあればその値になります。
        /// </summary>
        private CameraState ResolveCamera()
            => timeline is null
                ? CameraState.Default
                : SceneCameraResolver.Resolve(timeline, sceneCamera);

        private void OnSceneCameraPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SceneCamera.IsControlledByPreviewDrag) && sceneCamera.IsControlledByPreviewDrag)
                ResetToSceneCamera();
        }

        private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Timeline.CurrentFrame))
                UpdateSourceDescription();

            UpdatePreviewItems();
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            if (d3dHost is null || timeline is null)
                return;

            // シーンカメラがキーフレームで動いた場合、YMM4 の標準プレビューは
            // 自力では更新されないため、変化を検知して描き直しを促す。
            if (sceneCamera.IsControlledByPreviewDrag)
                freeCamera.Invalidate();

            refresher?.RefreshIfCameraChanged(timeline, sceneCamera);

            UpdatePreviewItems();
            d3dHost.RenderFrame();
        }

        private void OnRender(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            if (d3dHost?.RenderTargetView is not { } renderTarget
                || d3dHost.DepthStencilView is not { } depthStencil
                || timeline is null
                || sourceAndDevices is null
                || width <= 0 || height <= 0)
            {
                return;
            }

            var time = TimelineRefresher.GetTime(timeline);
            var camera = ResolveCamera();
            freeCamera.EnsureInitialized(camera);

            renderer.Draw(device, context, renderTarget, depthStencil, width, height, new PreviewScene
            {
                ViewPose = freeCamera.GetPose(),
                SceneCameraPose = camera.GetPose(),
                Time = time,
                Environment = new PreviewEnvironment(device, sourceAndDevices.Devices, scene, sourceDescription),
                Items = previewItems,
            });
        }

        private void OnMouseAction(Point position, D3D11Host.MouseEventKind kind, int delta)
        {
            if (timeline is null)
                return;

            freeCamera.EnsureInitialized(ResolveCamera());

            if (!freeCamera.HandleMouse(position, kind, delta))
                return;

            // シーンカメラを直接操作している場合だけ、視点の変化を出力側に反映する。
            if (!sceneCamera.IsControlledByPreviewDrag)
                return;

            freeCamera.ApplyTo(sceneCamera);
            refresher?.ForceRefresh(timeline, sceneCamera);
        }

        private void UpdateSourceDescription()
        {
            if (timeline is null || toolInfo is null)
                return;

            var info = timeline.VideoInfo;
            sourceDescription = new TimelineSourceDescription(
                new System.Drawing.Size(info.Width, info.Height),
                new YukkuriMovieMaker.Player.Video.FrameTime(timeline.CurrentFrame, info.FPS),
                new YukkuriMovieMaker.Player.Video.FrameTime(timeline.Length, info.FPS),
                info.FPS,
                TimelineSourceUsage.Playing,
                timeline.ID,
                toolInfo.Scenes?.AllScenes?.Cast<ISceneInfo>() ?? []);
        }

        private void UpdatePreviewItems()
        {
            if (timeline?.Items is not { } items)
                return;

            var frame = timeline.CurrentFrame;
            var updated = new List<PreviewItem>();

            // 半透明な板は深度を書き込まないため、重なりは描画順で決まる。
            // YMM4 と同じく、番号の小さいレイヤーから先に描いて奥に置く。
            var visible = items
                .OfType<IVideoItem>()
                .Where(item => frame >= item.Frame && frame < item.Frame + item.Length)
                .Where(item => !IsCamera(item))
                .OrderBy(item => item.Layer);

            foreach (var item in visible)
            {
                foreach (var provider in FindProviders(item))
                    updated.Add(new PreviewItem(provider, item, item.Frame, item.Length));
            }

            previewItems = updated;
        }

        /// <summary>
        /// カメラアイテムは撮る側なので、被写体として並べません。
        /// </summary>
        /// <remarks>
        /// 並べると、何も描かない板として既定の描画方法に回されます。絵は出ませんが
        /// 無駄に描画元を作ることになります。カメラの位置はガイドで表示されます。
        /// </remarks>
        private static bool IsCamera(IVideoItem item)
            => item is ShapeItem shape && shape.ShapeParameter is ISceneCameraSource;

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

            if (providers.Count == 0)
                return [renderer.DefaultProvider];

            return providers.Distinct();
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            disposer.Dispose();
            timeline = null;
            toolInfo = null;
            sourceAndDevices = null;
            previewItems.Clear();

            GC.SuppressFinalize(this);
        }
    }
}
