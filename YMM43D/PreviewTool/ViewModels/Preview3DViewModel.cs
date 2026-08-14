using System.Collections.Immutable;
using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Project.Items;
using YMM43D.Player;
using YMM43D.Plugin;
using YMM43D.PreviewTool.Views;
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
        private const int DefaultItemLength = 300;

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
        private IVideoItem? selected;
        private IItem? selectedMarker;
        private ImmutableList<IItem>? lastItems;

        private bool drivesSceneCamera;
        private bool insertsKeyFrame;
        private bool showsGrid = true;
        private bool hadSelectedItem;
        private bool isDisposed;

        public D3D11Host? D3DHost
        {
            get => d3dHost;
            private set => Set(ref d3dHost, value, nameof(D3DHost));
        }

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

        public bool InsertsKeyFrame
        {
            get => insertsKeyFrame;
            set => Set(ref insertsKeyFrame, value, nameof(InsertsKeyFrame));
        }

        public bool ShowsGrid
        {
            get => showsGrid;
            set => Set(ref showsGrid, value, nameof(ShowsGrid));
        }

        public bool CanAddItem => timeline is not null;

        public bool HasSelectedItem => timeline?.SelectedItems?.Any() == true;

        public ICommand ResetToSceneCameraCommand { get; }
        public ICommand AddCameraCommand { get; }
        public ICommand AddLightCommand { get; }
        public ICommand AddEnvironmentCommand { get; }
        public ICommand FocusSelectedCommand { get; }
        public ICommand ViewAllCommand { get; }
        public ICommand LevelRollCommand { get; }
        public ICommand ViewFromCommand { get; }
        public ActionCommand AddKeyFrameCommand { get; }

        public Preview3DViewModel()
        {
            disposer.Collect(renderer);
            sceneBuilder = new PreviewSceneBuilder(renderer.DefaultProvider);

            ResetToSceneCameraCommand = new ActionCommand(_ => true, _ => ResetToSceneCamera());
            AddCameraCommand = new ActionCommand(_ => CanAddItem, _ => AddItem(() => new CameraItem()));
            AddLightCommand = new ActionCommand(_ => CanAddItem, _ => AddItem(() => new LightItem()));
            AddEnvironmentCommand = new ActionCommand(_ => CanAddItem, _ => AddItem(() => new EnvironmentItem()));
            FocusSelectedCommand = new ActionCommand(_ => true, _ => FocusSelected());
            ViewAllCommand = new ActionCommand(_ => true, _ => ViewAll());
            LevelRollCommand = new ActionCommand(_ => true, _ => LevelRoll());
            ViewFromCommand = new ActionCommand(_ => true, p => ViewFrom(p as string));
            AddKeyFrameCommand = new ActionCommand(
                _ => HasSelectedItem,
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
            lastItems = null;
            renderer.ResetItemCaches();
            OnPropertyChanged(nameof(CanAddItem));

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

        public void ResetToSceneCamera()
        {
            freeCamera.Reset();
            freeCamera.EnsureInitialized(ResolveCamera());
        }

        public void FocusSelected()
        {
            var item = selected ?? timeline?.SelectedItems.OfType<IVideoItem>().FirstOrDefault();

            if (item is not null)
                Focus(item);
        }

        public void ViewAll() => Focus(null);

        private void Focus(IVideoItem? item)
        {
            if (renderer.GetBounds(item) is not { } bounds)
                return;

            ApplyCameraMove(basis => freeCamera.Focus(bounds, basis));
        }

        public void LevelRoll() => ApplyCameraMove(basis => FreeCameraController.LevelRoll(basis));

        public void ViewFrom(string? name)
        {
            if (Enum.TryParse<ViewDirection>(name, out var direction))
                ViewFrom(direction);
        }

        internal void ViewFrom(ViewDirection direction)
        {
            var (yaw, pitch) = ViewDirections.GetAngles(direction);

            ApplyCameraMove(basis => freeCamera.ViewFrom(yaw, pitch, basis));
        }

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

            if (!freeCamera.IsDragging)
                SeparateHistory();
        }

        private EditScope GetEditScope(IItem item)
            => insertsKeyFrame && timeline is not null
                ? EditScope.AtFrame(timeline.CurrentFrame - item.Frame)
                : EditScope.Whole;

        public void AddItem(Func<BaseItem> create)
        {
            if (timeline is null)
                return;

            var frame = timeline.CurrentFrame;

            for (var layer = 0; layer <= timeline.MaxLayer + 1; layer++)
            {
                var item = create();

                item.Frame = frame;
                item.Length = DefaultItemLength;
                item.Layer = layer;

                if (timeline.TryAddItems([item], frame, layer, true))
                    return;
            }
        }

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

            if (drivesSceneCamera && SceneCameraResolver.Find(timeline) is not null)
                freeCamera.Invalidate();

            refresher?.RefreshIfCameraChanged(timeline);

            SyncSelectionState();
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
                Lighting = SceneLightingResolver.Resolve(timeline),
                Time = time,
                Environment = new PreviewEnvironment(
                    device, sourceAndDevices.Devices, scene, sceneBuilder.SourceDescription),
                Items = sceneBuilder.Items,
                Selected = selected,
                Markers = SceneMarkerResolver.Resolve(timeline),
                SelectedMarker = selectedMarker,
                ShowsGrid = showsGrid,
                ActiveHandle = itemDrag.Handle,
            });
        }

        private void OnMouseAction(Point position, D3D11Host.MouseEventKind kind, int delta)
        {
            if (timeline is null)
                return;

            var boundary = kind is not D3D11Host.MouseEventKind.Move;

            if (boundary)
                SeparateHistory();

            if (!HandleItemDrag(position, kind))
            {
                var modifiers = D3D11Host.CurrentModifiers;

                ApplyCameraMove(basis =>
                    freeCamera.HandleMouse(position, kind, delta, modifiers, basis) ?? CameraMove.None);
            }

            if (boundary)
                SeparateHistory();
        }

        private void SeparateHistory() => toolInfo?.UndoRedoManager?.Record();

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

        private bool TryGrab(Point position)
        {
            var screen = ToVector(position);

            if (renderer.PickGizmo(screen) is var grabbed and not GizmoHandle.None
                && renderer.Gizmo is { } gizmo
                && renderer.CreateRay(screen) is { } gizmoRay)
            {
                if (renderer.GizmoMarker is { } held && CanGrab(held))
                {
                    return itemDrag.BeginMarker(
                        held.Source, held.ItemTime, gizmo.Origin, grabbed, gizmoRay,
                        freeCamera.State.Forward, GetEditScope(held.Item));
                }

                if (selected is not null)
                {
                    return itemDrag.Begin(
                        selected, gizmo.Origin, grabbed, gizmoRay, freeCamera.State.Forward,
                        GetEditScope(selected));
                }
            }

            if (renderer.PickMarker(screen) is { } placed && CanGrab(placed)
                && renderer.CreateRay(screen) is { } markerRay)
            {
                selected = null;
                selectedMarker = placed.Item;
                timeline?.SelectedItems = [placed.Item];

                return itemDrag.BeginMarker(
                    placed.Source, placed.ItemTime, placed.Marker.Position, GizmoHandle.Free, markerRay,
                    freeCamera.State.Forward, GetEditScope(placed.Item));
            }

            // 追従中は画面いっぱいがカメラの写す絵になるので、どこを押しても
            // 何かのアイテムに当たる。それではカメラを振る手が残らない。
            // 狙って掴む軸ハンドルは上で拾ってあるので、ここは素通しでよい。
            if (IsDrivingCamera)
            {
                selected = null;
                return false;
            }

            // 掴む判定はアイテムを描き直して行う。描画の内側でしかできないので、
            // ここで1枚描かせて、その結果を受け取る。
            renderer.RequestPick(screen);
            d3dHost?.RenderFrame();

            if (renderer.TakePickResult() is not { } picked
                || renderer.CreateRay(screen) is not { } ray)
            {
                selected = null;
                return false;
            }

            selected = picked.Item;

            timeline?.SelectedItems = [picked.Item];

            return itemDrag.Begin(
                picked.Item, picked.World.Translation, GizmoHandle.Free, ray, freeCamera.State.Forward,
                GetEditScope(picked.Item));
        }

        // 追従を入れていても、その区間にカメラアイテムが無ければ動かす相手がいない。
        // そのときは追従していないときと同じに振る舞う。
        private bool IsDrivingCamera
            => drivesSceneCamera && timeline is not null && SceneCameraResolver.Find(timeline) is not null;

        // カメラ追従中のドラッグは、そのカメラを振る操作そのもの。目印として掴めると
        // 向きを変える手と場所を取り合ってしまうので、追従しているカメラは掴ませない。
        private bool CanGrab(in SceneMarkerResolver.PlacedMarker marker)
            => !IsDrivingCamera || marker.Marker.Kind != MarkerKind.Camera;

        private static Vector2 ToVector(Point position) => new((float)position.X, (float)position.Y);

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

                case Key.G when modifiers == ModifierKeys.None:
                    ShowsGrid = !ShowsGrid;
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
                    return TryHostKey(key, modifiers);
            }
        }

        private bool TryHostKey(Key key, ModifierKeys modifiers)
        {
            if (HostCommands.Matches(CommandType.Undo, key, modifiers))
                return TryUndoRedo(redo: false);

            if (HostCommands.Matches(CommandType.Redo, key, modifiers))
                return TryUndoRedo(redo: true);

            return false;
        }

        private bool TryUndoRedo(bool redo)
        {
            if (toolInfo?.UndoRedoManager is not { } manager)
                return false;

            if (redo ? manager.IsRedoable : manager.IsUndoable)
                _ = redo ? manager.RedoAsync() : manager.UndoAsync();

            return true;
        }

        private void SyncSelectionState()
        {
            selectedMarker = timeline?.SelectedItems?.FirstOrDefault(item => item is ISceneMarkerSource);

            var has = HasSelectedItem;

            if (has == hadSelectedItem)
                return;

            hadSelectedItem = has;
            OnPropertyChanged(nameof(HasSelectedItem));
            AddKeyFrameCommand.RaiseCanExecuteChanged();
        }

        private void UpdateSourceDescription()
        {
            if (timeline is not null && toolInfo is not null)
                sceneBuilder.UpdateSource(timeline, toolInfo);
        }

        private void UpdatePreviewItems()
        {
            if (timeline is null)
                return;

            // Items は差し替え式なので、並びが変われば別のものになる。変わったなら
            // YMM4 側が合成を組み直しているので、こちらが抱えている絵は捨てる。
            // 古いまま触ると、解放済みの領域へ飛んでプロセスごと落ちる。
            if (!ReferenceEquals(lastItems, timeline.Items))
            {
                lastItems = timeline.Items;
                renderer.ResetItemCaches();
            }

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
