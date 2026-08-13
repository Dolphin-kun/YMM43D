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
using YukkuriMovieMaker.Settings;

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
        private readonly PreviewSceneBuilder sceneBuilder;

        private TimelineRefresher? refresher;
        private Timeline? timeline;
        private TimelineToolInfo? toolInfo;
        private D3D11Host? d3dHost;
        private Scene? scene;
        private TimelineSourceAndDevices? sourceAndDevices;
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
        public ICommand AddKeyFrameCommand { get; }

        public Preview3DViewModel()
        {
            disposer.Collect(renderer);
            sceneBuilder = new PreviewSceneBuilder(renderer.DefaultProvider);

            ResetToSceneCameraCommand = new ActionCommand(_ => true, _ => ResetToSceneCamera());
            AddCameraCommand = new ActionCommand(_ => CanAddCamera, _ => AddCamera());
            FocusSelectedCommand = new ActionCommand(_ => true, _ => FocusSelected());
            ViewAllCommand = new ActionCommand(_ => true, _ => ViewAll());
            LevelRollCommand = new ActionCommand(_ => true, _ => LevelRoll());
            ViewFromCommand = new ActionCommand(_ => true, p => ViewFrom(p as string));
            // 使えるかどうかは本体側の選択で決まり、こちらからは追えない。
            // 常に押せるようにしておき、できないときは何も起きないだけにする。
            AddKeyFrameCommand = new ActionCommand(
                _ => true,
                _ => HostCommands.Execute(CommandType.AddKeyFrameAtCurrentFrame, d3dHost));
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
        /// <param name="name"><see cref="ViewDirection"/> の名前。</param>
        public void ViewFrom(string? name)
        {
            if (Enum.TryParse<ViewDirection>(name, out var direction))
                ViewFrom(direction);
        }

        /// <summary>決まった向きから見ます。</summary>
        internal void ViewFrom(ViewDirection direction)
        {
            var (yaw, pitch) = ViewDirections.GetAngles(direction);

            ApplyCameraMove(basis => freeCamera.ViewFrom(yaw, pitch, basis));
        }

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

            // ドラッグの区切りはマウスを離したときに入れる。キーやホイールのように
            // 1回で終わる操作は、ここで区切らないと履歴に残らない。
            if (!freeCamera.IsDragging)
                SeparateHistory();
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
                Environment = new PreviewEnvironment(
                    device, sourceAndDevices.Devices, scene, sceneBuilder.SourceDescription),
                Items = sceneBuilder.Items,
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

            // 押した所と離した所で、元に戻す履歴を区切る。動かしている最中は区切らない
            // ので、ドラッグ1回ぶんが 1 手にまとまる。
            var boundary = kind is not D3D11Host.MouseEventKind.Move;

            if (boundary)
                SeparateHistory();

            if (!HandleItemDrag(position, kind))
            {
                var modifiers = D3D11Host.CurrentModifiers;

                // 押した瞬間はドラッグの種類を覚えるだけで、動く量は返ってこない。
                // その場合に相手を探しに行っても無駄なので、差分が出てから振り分ける。
                ApplyCameraMove(basis =>
                    freeCamera.HandleMouse(position, kind, delta, modifiers, basis) ?? CameraMove.None);
            }

            if (boundary)
                SeparateHistory();
        }

        /// <summary>
        /// 元に戻す履歴に区切りを入れます。
        /// </summary>
        /// <remarks>
        /// YMM4 は変更を積み上げておき、区切ったところまでを 1 手として覚えます。
        /// 区切らないまま変更すると、その変更は履歴に入らず、直前の 1 手を戻したときに
        /// 一緒に巻き戻ってしまいます（アイテムを足して動かした後に戻すと、移動ではなく
        /// アイテムごと消える、という形で出ます）。
        /// </remarks>
        private void SeparateHistory() => toolInfo?.UndoRedoManager?.Record();

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
                    ViewFrom(control ? ViewDirection.Back : ViewDirection.Front);
                    return true;

                case Key.NumPad3 or Key.D3:
                    ViewFrom(control ? ViewDirection.Left : ViewDirection.Right);
                    return true;

                case Key.NumPad7 or Key.D7:
                    ViewFrom(control ? ViewDirection.Bottom : ViewDirection.Top);
                    return true;

                default:
                    // 割り当ての決まっていないキーだけ、本体の操作に回す。先に回すと、
                    // 本体側で同じキーを使っている操作にプレビューの割り当てが負ける。
                    return TryHostKey(key, modifiers);
            }
        }

        /// <summary>
        /// 本体に割り当てられている操作のうち、プレビュー上でも効かせたいもの。
        /// </summary>
        private bool TryHostKey(Key key, ModifierKeys modifiers)
        {
            if (HostCommands.Matches(CommandType.Undo, key, modifiers))
                return TryUndoRedo(redo: false);

            if (HostCommands.Matches(CommandType.Redo, key, modifiers))
                return TryUndoRedo(redo: true);

            return false;
        }

        /// <summary>
        /// 元に戻す・やり直しを本体に依頼します。
        /// </summary>
        /// <remarks>
        /// 本体のコマンドではなく履歴そのものを直接動かします。コマンドは送り先の
        /// 要素をたどって処理されるので、子ウィンドウが入力を受けているあいだは
        /// 届くとは限りません。
        /// <para>
        /// できることが無くても受け取ったことにします。ここで見送ると、同じキーで
        /// 本体側の操作が動いてしまい、効いたり効かなかったりします。
        /// </para>
        /// </remarks>
        private bool TryUndoRedo(bool redo)
        {
            if (toolInfo?.UndoRedoManager is not { } manager)
                return false;

            if (redo ? manager.IsRedoable : manager.IsUndoable)
                _ = redo ? manager.RedoAsync() : manager.UndoAsync();

            return true;
        }

        private void UpdateSourceDescription()
        {
            if (timeline is not null && toolInfo is not null)
                sceneBuilder.UpdateSource(timeline, toolInfo);
        }

        private void UpdatePreviewItems()
        {
            if (timeline is not null)
                sceneBuilder.UpdateItems(timeline);
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
            sceneBuilder.Clear();

            GC.SuppressFinalize(this);
        }
    }
}
