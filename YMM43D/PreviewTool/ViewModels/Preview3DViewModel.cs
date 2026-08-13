using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Camera;
using YMM43D.Commons;
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
        /// <summary>カメラアイテムを置くときの既定の長さ（フレーム）。</summary>
        private const int DefaultCameraLength = 300;

        private readonly DisposeCollector disposer = new();
        private readonly Preview3DRenderer renderer = new();
        private readonly FreeCameraController freeCamera = new();
        private readonly ItemDragController itemDrag = new();

        private TimelineRefresher? refresher;
        private Timeline? timeline;
        private TimelineToolInfo? toolInfo;
        private D3D11Host? d3dHost;
        private Scene? scene;
        private TimelineSourceAndDevices? sourceAndDevices;
        private TimelineSourceDescription? sourceDescription;
        private (int Width, int Height, int Fps, int Frame, int Length) lastSourceSignature;
        private List<PreviewItem> previewItems = [];
        // 案内を出しているアイテム。タイムライン側の選択とは別に持つ。プレビューで
        // 掴んだものだけに案内を出したいので、他の経路で選んだものには反応させない。
        private IVideoItem? selected;

        private bool drivesSceneCamera;
        private bool insertsKeyFrame;
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

        /// <summary>
        /// <c>true</c> のとき、ドラッグがいまの再生位置に中間点を打ち、そこだけを動かします。
        /// </summary>
        /// <remarks>
        /// 切ってあるあいだは、打ってあるキーフレームすべてが同じだけ動きます。動きを
        /// 壊さずに置き直せますが、それだけでは動きを作れません。入れると、止めた位置で
        /// 少しずつ形を決めていく作り方ができます。
        /// </remarks>
        public bool InsertsKeyFrame
        {
            get => insertsKeyFrame;
            set => Set(ref insertsKeyFrame, value, nameof(InsertsKeyFrame));
        }

        /// <summary>カメラアイテムを置ける状態かどうか。</summary>
        public bool CanAddCamera => timeline is not null;

        public ICommand ResetToSceneCameraCommand { get; }
        public ICommand AddCameraCommand { get; }
        public ICommand FocusSelectedCommand { get; }
        public ICommand ViewAllCommand { get; }
        public ICommand LevelRollCommand { get; }
        public ICommand ViewFromCommand { get; }

        public Preview3DViewModel()
        {
            disposer.Collect(renderer);

            ResetToSceneCameraCommand = new ActionCommand(_ => true, _ => ResetToSceneCamera());
            AddCameraCommand = new ActionCommand(_ => CanAddCamera, _ => AddCamera());
            FocusSelectedCommand = new ActionCommand(_ => true, _ => FocusSelected());
            ViewAllCommand = new ActionCommand(_ => true, _ => ViewAll());
            LevelRollCommand = new ActionCommand(_ => true, _ => LevelRoll());
            ViewFromCommand = new ActionCommand(_ => true, p => ViewFrom(p as string));
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
            host.KeyHandler = HandleKey;
            disposer.CollectAction(host, () =>
            {
                host.Render -= OnRender;
                host.MouseAction -= OnMouseAction;
                host.KeyHandler = null;
            });

            CompositionTarget.Rendering += OnCompositionRendering;
            disposer.CollectAction(this, () => CompositionTarget.Rendering -= OnCompositionRendering);

            UpdatePreviewItems();
        }

        /// <summary>見る位置を、いまシーンを撮っているカメラに戻します。</summary>
        public void ResetToSceneCamera()
        {
            freeCamera.Reset();
            freeCamera.EnsureInitialized(ResolveCamera());
        }

        /// <summary>
        /// 選んでいるアイテムが画面に収まるまで寄ります。
        /// </summary>
        /// <remarks>
        /// プレビューで掴んだものを優先し、無ければタイムラインで選んでいるものを見ます。
        /// どちらも無いときは何もしません。見当違いの所へ飛ぶより、動かない方が分かります。
        /// </remarks>
        public void FocusSelected()
        {
            var item = selected ?? timeline?.SelectedItems.OfType<IVideoItem>().FirstOrDefault();

            if (item is not null)
                Focus(item);
        }

        /// <summary>出ているものが全部入るまで引きます。</summary>
        public void ViewAll() => Focus(null);

        private void Focus(IVideoItem? item)
        {
            if (renderer.GetBounds(item) is not { } bounds)
                return;

            ApplyCameraMove(basis => freeCamera.Focus(bounds, basis));
        }

        /// <summary>傾きを 0 に戻します。</summary>
        public void LevelRoll() => ApplyCameraMove(basis => FreeCameraController.LevelRoll(basis));

        /// <summary>決まった向きから見ます。</summary>
        /// <param name="name"><see cref="PresetViews"/> の名前。</param>
        public void ViewFrom(string? name)
        {
            if (name is null || !PresetViews.TryGetValue(name, out var view))
                return;

            ApplyCameraMove(basis => freeCamera.ViewFrom(view.Yaw, view.Pitch, basis));
        }

        /// <summary>
        /// 決まった向きから見るときの、水平・垂直の回転角。
        /// </summary>
        /// <remarks>
        /// 真上・真下は視線が定まらなくなる手前で止めます。ちょうど真上から見ると
        /// 水平方向の向きが決められません。
        /// </remarks>
        private static readonly Dictionary<string, (float Yaw, float Pitch)> PresetViews = new()
        {
            ["Front"] = (0f, 0f),
            ["Back"] = (180f, 0f),
            ["Right"] = (90f, 0f),
            ["Left"] = (-90f, 0f),
            ["Top"] = (0f, -CameraState.MaxPitch),
            ["Bottom"] = (0f, CameraState.MaxPitch),
        };

        /// <summary>
        /// カメラの動きを、いま動かすべき相手に効かせます。
        /// </summary>
        /// <remarks>
        /// 「カメラ追従」が入っていればタイムラインのカメラアイテムを、そうでなければ
        /// プレビュー専用の視点を動かします。動かす量はどちらの場合も相手の現在値から
        /// 決めるので、<paramref name="make"/> にはその値が渡ります。
        /// </remarks>
        private void ApplyCameraMove(Func<CameraState, CameraMove> make)
        {
            if (timeline is null)
                return;

            var active = drivesSceneCamera ? SceneCameraResolver.Find(timeline) : null;
            var basis = active is { } found ? found.Source.GetState(found.ItemTime) : freeCamera.State;

            freeCamera.EnsureInitialized(basis);

            var move = make(basis);
            if (move.IsZero)
                return;

            if (active is not { } target)
            {
                freeCamera.Apply(move);
                return;
            }

            target.Source.Move(move, GetEditScope(target.Item));
            refresher?.ForceRefresh(timeline);
        }

        /// <summary>
        /// そのアイテムを動かした結果を、アニメーションのどこに書き込むか。
        /// </summary>
        /// <remarks>
        /// キーフレームはアイテムの先頭からの位置で打たれるので、タイムライン上の
        /// 再生位置をアイテム内の位置に直します。
        /// </remarks>
        private EditScope GetEditScope(IItem item)
            => insertsKeyFrame && timeline is not null
                ? EditScope.AtFrame(timeline.CurrentFrame - item.Frame)
                : EditScope.Whole;

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

            var frame = timeline.CurrentFrame;

            for (var layer = 0; layer <= timeline.MaxLayer + 1; layer++)
            {
                var item = new CameraItem
                {
                    Frame = frame,
                    Length = DefaultCameraLength,
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

            // 画面の大きさは編集中に変えられる。カメラの枠の縦横比がそれで決まるので、
            // フレームが進んでいなくても追いかける。
            UpdateSourceDescription();
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
                SceneCamera = camera,
                Time = time,
                Environment = new PreviewEnvironment(device, sourceAndDevices.Devices, scene, sourceDescription),
                Items = previewItems,
                Selected = selected,
                ActiveHandle = itemDrag.Handle,
            });
        }

        /// <summary>
        /// マウス操作を振り分けます。
        /// </summary>
        /// <remarks>
        /// 左ドラッグは、アイテムの上から始めればそのアイテムを動かし、何も無い所から
        /// 始めればカメラを回します。同じボタンで両方できるのは、掴む物があるかどうかが
        /// 押した瞬間に決まるからです。アイテムの上でもカメラを回したいときは
        /// Alt を押しながらドラッグしてください。中ドラッグ・右ドラッグ・ホイールは
        /// 常にカメラです。
        /// <para>
        /// 左と中は修飾キーで意味が変わります。Shift で平行移動、Ctrl で傾きです。
        /// </para>
        /// </remarks>
        private void OnMouseAction(Point position, D3D11Host.MouseEventKind kind, int delta)
        {
            if (timeline is null)
                return;

            if (HandleItemDrag(position, kind))
                return;

            var modifiers = D3D11Host.CurrentModifiers;

            // 押した瞬間はドラッグの種類を覚えるだけで、動く量は返ってこない。
            // その場合に相手を探しに行っても無駄なので、差分が出てから振り分ける。
            ApplyCameraMove(basis =>
                freeCamera.HandleMouse(position, kind, delta, modifiers, basis) ?? CameraMove.None);
        }

        /// <summary>
        /// アイテムを掴む・動かす・離すを処理します。
        /// </summary>
        /// <returns>アイテムの操作として扱ったら <c>true</c>。カメラには渡しません。</returns>
        private bool HandleItemDrag(Point position, D3D11Host.MouseEventKind kind)
        {
            switch (kind)
            {
                case D3D11Host.MouseEventKind.Down:
                    if ((D3D11Host.CurrentModifiers & ModifierKeys.Alt) != 0)
                        return false;

                    return TryGrab(position);

                case D3D11Host.MouseEventKind.Move when itemDrag.IsDragging:
                    if (renderer.CreateRay(ToVector(position)) is { } ray && itemDrag.Update(ray))
                        refresher?.ForceRefresh(timeline!);

                    return true;

                case D3D11Host.MouseEventKind.Up when itemDrag.IsDragging:
                    itemDrag.End();
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 押した所にあるものを掴みます。
        /// </summary>
        /// <remarks>
        /// 案内の矢印や輪を先に見ます。アイテムより手前に出ているので、重なって
        /// いるときは案内の方を掴めた方が扱いやすいためです。
        /// </remarks>
        private bool TryGrab(Point position)
        {
            var screen = ToVector(position);

            if (selected is not null
                && renderer.PickGizmo(screen) is var grabbed and not GizmoHandle.None
                && renderer.Gizmo is { } gizmo
                && renderer.CreateRay(screen) is { } gizmoRay)
            {
                return itemDrag.Begin(
                    selected, gizmo.Origin, grabbed, gizmoRay, freeCamera.State.Forward,
                    GetEditScope(selected));
            }

            if (renderer.Pick(screen, out var ray) is not { } picked)
            {
                selected = null;
                return false;
            }

            selected = picked.Item;

            // 掴んだものを選択しておくと、右のプロパティ欄がそのアイテムに切り替わる。
            if (timeline is not null)
                timeline.SelectedItems = [picked.Item];

            return itemDrag.Begin(
                picked.Item, picked.World.Translation, GizmoHandle.Free, ray, freeCamera.State.Forward,
                GetEditScope(picked.Item));
        }

        private static Vector2 ToVector(Point position) => new((float)position.X, (float)position.Y);

        /// <summary>
        /// プレビュー上のキー操作。
        /// </summary>
        /// <returns>受け取ったキーなら <c>true</c>。呼び出し側はそこで止めてください。</returns>
        /// <remarks>
        /// 3D表示は子ウィンドウなので、WPF のキー入力はそのままでは届きません。
        /// <see cref="D3D11Host"/> が拾ったものと、枠の側で拾ったものの両方がここに来ます。
        /// <para>
        /// 数字はテンキーと本体側のどちらでも効くようにしています。テンキーの無い
        /// キーボードでも同じように使えます。
        /// </para>
        /// </remarks>
        public bool HandleKey(Key key, ModifierKeys modifiers)
        {
            var control = (modifiers & ModifierKeys.Control) != 0;
            var shift = (modifiers & ModifierKeys.Shift) != 0;

            switch (key)
            {
                case Key.R when control:
                    ResetToSceneCamera();
                    return true;

                case Key.R when shift:
                    LevelRoll();
                    return true;

                case Key.F when modifiers == ModifierKeys.None:
                    FocusSelected();
                    return true;

                case Key.Home when modifiers == ModifierKeys.None:
                    ViewAll();
                    return true;

                case Key.K when modifiers == ModifierKeys.None:
                    InsertsKeyFrame = !InsertsKeyFrame;
                    return true;

                case Key.NumPad0 or Key.D0:
                    DrivesSceneCamera = !DrivesSceneCamera;
                    return true;

                case Key.NumPad1 or Key.D1:
                    ViewFrom(control ? "Back" : "Front");
                    return true;

                case Key.NumPad3 or Key.D3:
                    ViewFrom(control ? "Left" : "Right");
                    return true;

                case Key.NumPad7 or Key.D7:
                    ViewFrom(control ? "Bottom" : "Top");
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 描画元の情報を、いまのタイムラインの設定に合わせます。
        /// </summary>
        /// <remarks>
        /// 画面の大きさや FPS は編集中に変えられます。作り直しは安くないので、
        /// 前回と違うときだけ組み立て直します。
        /// </remarks>
        private void UpdateSourceDescription()
        {
            if (timeline is null || toolInfo is null)
                return;

            var info = timeline.VideoInfo;
            var signature = (info.Width, info.Height, info.FPS, timeline.CurrentFrame, timeline.Length);

            if (sourceDescription is not null && signature == lastSourceSignature)
                return;

            lastSourceSignature = signature;
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
                .Where(item => !item.IsHidden)
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
