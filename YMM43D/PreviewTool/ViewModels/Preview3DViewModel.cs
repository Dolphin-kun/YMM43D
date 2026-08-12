using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Camera;
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
        /// <summary>カメラアイテムを置くときの既定の長さ（秒）。</summary>
        private const int DefaultCameraSeconds = 5;

        private readonly DisposeCollector disposer = new();
        private readonly Preview3DRenderer renderer = new();
        private readonly FreeCameraController freeCamera = new();

        private TimelineRefresher? refresher;
        private Timeline? timeline;
        private TimelineToolInfo? toolInfo;
        private D3D11Host? d3dHost;
        private Scene? scene;
        private TimelineSourceAndDevices? sourceAndDevices;
        private TimelineSourceDescription? sourceDescription;
        private List<PreviewItem> previewItems = [];
        private bool drivesSceneCamera = true;
        private bool isDisposed;

        public D3D11Host? D3DHost
        {
            get => d3dHost;
            private set => Set(ref d3dHost, value, nameof(D3DHost));
        }

        /// <summary>
        /// <c>true</c> のとき、プレビュー上のドラッグがカメラアイテムを直接動かします。
        /// <c>false</c> のときは見る位置だけが動き、出力には影響しません。
        /// </summary>
        public bool DrivesSceneCamera
        {
            get => drivesSceneCamera;
            set
            {
                if (!Set(ref drivesSceneCamera, value, nameof(DrivesSceneCamera)))
                    return;

                freeCamera.Invalidate();
            }
        }

        /// <summary>カメラアイテムを置ける状態かどうか。</summary>
        public bool CanAddCamera => timeline is not null;

        public ICommand ResetToSceneCameraCommand { get; }
        public ICommand AddCameraCommand { get; }

        public Preview3DViewModel()
        {
            disposer.Collect(renderer);

            ResetToSceneCameraCommand = new ActionCommand(_ => true, _ => ResetToSceneCamera());
            AddCameraCommand = new ActionCommand(_ => CanAddCamera, _ => AddCamera());
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
            OnPropertyChanged(nameof(CanAddCamera));

            if (timeline is null)
                return;

            scene = info.Scenes?.AllScenes.FirstOrDefault(s => s.Timeline == timeline);
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

        /// <summary>見る位置を、いまシーンを撮っているカメラに戻します。</summary>
        public void ResetToSceneCamera()
        {
            freeCamera.Invalidate();
            freeCamera.EnsureInitialized(ResolveCamera());
        }

        /// <summary>
        /// いまの再生位置に、3Dカメラのアイテムを置きます。
        /// </summary>
        /// <remarks>
        /// 空いているレイヤーを下から探します。<c>TryAddItems</c> は他のアイテムと
        /// 重なると失敗するので、置けるまで順に試します。
        /// </remarks>
        public void AddCamera()
        {
            if (timeline is null)
                return;

            var fps = Math.Max(1, timeline.VideoInfo.FPS);
            var frame = timeline.CurrentFrame;

            for (var layer = 0; layer <= timeline.MaxLayer + 1; layer++)
            {
                var item = new CameraItem
                {
                    Frame = frame,
                    Length = Math.Min(fps * DefaultCameraSeconds, Math.Max(1, timeline.Length - frame)),
                    Layer = layer,
                };

                if (timeline.TryAddItems([item], frame, layer, true))
                    return;
            }
        }

        /// <summary>いまシーンを撮っているカメラ。アイテムが無ければ既定のカメラ。</summary>
        private CameraState ResolveCamera()
            => timeline is null ? CameraState.Default : SceneCameraResolver.Resolve(timeline);

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

            // カメラアイテムを動かしているあいだは、見る位置もそのカメラに追従させる。
            // アイテムが無いときまで追従させると、自由に見て回るための視点が
            // 毎フレーム既定の位置へ戻され、ドラッグしても動かなくなる。
            if (drivesSceneCamera && SceneCameraResolver.Find(timeline) is not null)
                freeCamera.Invalidate();

            refresher?.RefreshIfCameraChanged(timeline);

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

            // カメラを直接動かす場合の基準は、いま効いているカメラアイテムの値。
            // 平行移動の向きと大きさがそこから決まる。
            var active = drivesSceneCamera ? SceneCameraResolver.Find(timeline) : null;
            var basis = active is { } found ? found.Camera.GetState(found.ItemTime) : freeCamera.State;

            freeCamera.EnsureInitialized(basis);

            if (freeCamera.HandleMouse(position, kind, delta, basis) is not { } move || move.IsZero)
                return;

            if (active is not { } target)
            {
                freeCamera.Apply(move);
                return;
            }

            target.Camera.Move(move);
            refresher?.ForceRefresh(timeline);
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
                .OrderBy(item => item.Layer);

            foreach (var item in visible)
            {
                foreach (var provider in FindProviders(item))
                    updated.Add(new PreviewItem(provider, item, item.Frame, item.Length));
            }

            previewItems = updated;
        }

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
