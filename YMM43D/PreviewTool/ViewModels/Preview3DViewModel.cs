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
using YMM43D.PreviewTool.Views;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.ItemEditor;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace YMM43D.PreviewTool.ViewModels
{
    public class Preview3DViewModel : Bindable, ITimelineToolViewModel, IDisposable
    {
        private const int DefaultItemLength = 300;

        private const double ClickSlop = 4;

        // 何かが変わった直後は、YMM4 側の描き直しが追いつくまで少しのあいだ描き続ける。
        private const long SettleMs = 500;

        private const long SettleIntervalMs = 33;

        // 何も起きていないときの見回りの間隔。取りこぼした変化もこの間隔で拾う。
        private const long IdleIntervalMs = 1000;

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
        private IReadOnlyList<IVideoItem> selection = [];
        private IItem? selectedMarker;
        private ImmutableList<IItem>? lastItems;
        private PreviewScene? preparedScene;
        private Point? toggleClickAt;

        private bool drivesSceneCamera;
        private bool insertsKeyFrame;
        private bool showsGrid = true;
        private bool snapsToGrid;
        private float snapStep = SnapGrid.DefaultStep;
        private bool hadSelectedItem;
        private bool isDisposed;

        private bool renderRequested = true;
        private long settleUntil;
        private long lastRenderAt;
        private long lastRevision = -1;
        private Size lastHostSize;

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

                if (value)
                    EnsureSceneCamera();

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

        public bool SnapsToGrid
        {
            get => snapsToGrid;
            set => Set(ref snapsToGrid, value, nameof(SnapsToGrid));
        }

        public bool SnapsEvery10
        {
            get => snapStep == 10f;
            set => SetSnapStep(value, 10f);
        }

        public bool SnapsEvery50
        {
            get => snapStep == 50f;
            set => SetSnapStep(value, 50f);
        }

        public bool SnapsEvery100
        {
            get => snapStep == 100f;
            set => SetSnapStep(value, 100f);
        }

        private void SetSnapStep(bool chosen, float step)
        {
            if (chosen)
                snapStep = step;

            OnPropertyChanged(nameof(SnapsEvery10));
            OnPropertyChanged(nameof(SnapsEvery50));
            OnPropertyChanged(nameof(SnapsEvery100));
        }

        public UpdateChecker Update => UpdateChecker.Instance;

        public bool CanAddItem => timeline is not null;

        public bool HasSelectedItem => timeline?.SelectedItems is { IsEmpty: false };

        public ICommand ResetToSceneCameraCommand { get; }
        public ICommand AddCameraCommand { get; }
        public ICommand AddLightCommand { get; }
        public ICommand AddEnvironmentCommand { get; }
        public ICommand FocusSelectedCommand { get; }
        public ICommand ViewAllCommand { get; }
        public ICommand LevelRollCommand { get; }
        public ICommand ViewFromCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ActionCommand AddKeyFrameCommand { get; }

        public Preview3DViewModel()
        {
            disposer.Collect(renderer);
            sceneBuilder = new PreviewSceneBuilder(renderer.DefaultProvider);
            PropertyChanged += (_, _) => RequestRender();
            UpdateChecker.Instance.EnsureChecked();

            ResetToSceneCameraCommand = new ActionCommand(_ => true, _ => ResetToSceneCamera());
            AddCameraCommand = new ActionCommand(_ => CanAddItem, _ => AddItem(() => new CameraItem()));
            AddLightCommand = new ActionCommand(_ => CanAddItem, _ => AddItem(() => new LightItem()));
            AddEnvironmentCommand = new ActionCommand(_ => CanAddItem, _ => AddItem(() => new EnvironmentItem()));
            FocusSelectedCommand = new ActionCommand(_ => true, _ => FocusSelected());
            ViewAllCommand = new ActionCommand(_ => true, _ => ViewAll());
            LevelRollCommand = new ActionCommand(_ => true, _ => LevelRoll());
            ViewFromCommand = new ActionCommand(_ => true, p => ViewFrom(p as string));
            SelectAllCommand = new ActionCommand(_ => true, _ => SelectAll());
            ClearSelectionCommand = new ActionCommand(_ => true, _ => ClearSelection());
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
            preparedScene = null;

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
            timeline.UndoRedoCommandCreated += OnTimelineEdited;
            disposer.CollectAction(timeline, () =>
            {
                timeline.PropertyChanged -= OnTimelinePropertyChanged;
                timeline.UndoRedoCommandCreated -= OnTimelineEdited;
            });

            if (info.UndoRedoManager is { } manager)
            {
                manager.Undoed += OnHistoryApplied;
                manager.Redoed += OnHistoryApplied;
                disposer.CollectAction(manager, () =>
                {
                    manager.Undoed -= OnHistoryApplied;
                    manager.Redoed -= OnHistoryApplied;
                });
            }

            var host = new D3D11Host();
            D3DHost = host;
            disposer.Collect(host);

            host.Preparing += OnPreparing;
            host.Render += OnRender;
            host.MouseAction += OnMouseAction;
            host.KeyHandler = HandleKey;
            disposer.CollectAction(host, () =>
            {
                host.Preparing -= OnPreparing;
                host.Render -= OnRender;
                host.MouseAction -= OnMouseAction;
                host.KeyHandler = null;
            });

            CompositionTarget.Rendering += OnCompositionRendering;
            disposer.CollectAction(this, () => CompositionTarget.Rendering -= OnCompositionRendering);

            RequestRender();
        }

        public void ResetToSceneCamera()
        {
            RequestRender();
            freeCamera.Reset();
            freeCamera.EnsureInitialized(ResolveCamera());
        }

        public void FocusSelected()
        {
            if (selection.Count > 0)
                Focus(selection);
        }

        public void ViewAll() => Focus(null);

        private void Focus(IReadOnlyCollection<IVideoItem>? items)
        {
            if (renderer.GetBounds(items) is not { } bounds)
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

            RequestRender();

            if (active is not { } target)
            {
                freeCamera.Apply(move);
                return;
            }

            target.Source.Move(move, target.ItemTime, GetEditScope(target.Item));
            refresher?.ForceRefresh(timeline);

            if (!freeCamera.IsDragging)
                SeparateHistory();
        }

        private EditScope GetEditScope(IItem item)
            => insertsKeyFrame && timeline is not null
                ? EditScope.AtFrame(timeline.CurrentFrame - item.Frame)
                : EditScope.Whole;

        private void EnsureSceneCamera()
        {
            if (timeline is null || SceneCameraResolver.Find(timeline) is not null)
                return;

            AddItem(() => new CameraItem());
        }

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

        private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e) => RequestRender();

        private void OnTimelineEdited(object? sender, EventArgs e) => RequestRender();

        private void OnHistoryApplied(object? sender, EventArgs e) => RequestRender();

        private void RequestRender()
        {
            renderRequested = true;
            settleUntil = Environment.TickCount64 + SettleMs;
        }

        private bool ShouldRender(D3D11Host host)
        {
            var revision = SceneRevision.Current;

            if (revision != lastRevision)
            {
                lastRevision = revision;
                RequestRender();
            }

            var size = new Size(host.ActualWidth, host.ActualHeight);

            if (size != lastHostSize)
            {
                lastHostSize = size;
                RequestRender();
            }

            if (renderRequested)
                return true;

            var now = Environment.TickCount64;
            var interval = now < settleUntil ? SettleIntervalMs : IdleIntervalMs;

            return now - lastRenderAt >= interval;
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            if (d3dHost is null || timeline is null || !ShouldRender(d3dHost))
                return;

            renderRequested = false;
            lastRenderAt = Environment.TickCount64;

            using (SceneRevision.Mute())
            {
                if (drivesSceneCamera && SceneCameraResolver.Find(timeline) is not null)
                    freeCamera.Invalidate();

                refresher?.RefreshIfCameraChanged(timeline);

                SyncSelectionState();
                UpdateSourceDescription();
                UpdatePreviewItems();
                d3dHost.RenderFrame();
            }
        }

        private void OnPreparing(ID3D11Device device)
        {
            preparedScene = CreatePreviewScene(device);

            if (preparedScene is not null)
                renderer.Prepare(preparedScene);
        }

        private void OnRender(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            if (d3dHost?.RenderTargetView is not { } renderTarget
                || d3dHost.DepthStencilView is not { } depthStencil
                || preparedScene is not { } prepared
                || width <= 0 || height <= 0)
            {
                return;
            }

            renderer.Draw(device, context, renderTarget, depthStencil, width, height, prepared);
        }

        private PreviewScene? CreatePreviewScene(ID3D11Device device)
        {
            if (timeline is null || sourceAndDevices is null)
                return null;

            var time = TimelineRefresher.GetTime(timeline);
            var camera = ResolveCamera();
            freeCamera.EnsureInitialized(camera);

            return new PreviewScene
            {
                ViewPose = freeCamera.GetPose(),
                SceneCamera = camera,
                Lighting = SceneLightingResolver.Resolve(timeline),
                Time = time,
                Environment = new PreviewEnvironment(
                    device, sourceAndDevices.Devices, scene, sceneBuilder.SourceDescription),
                Items = sceneBuilder.Items,
                Selection = selection,
                Markers = SceneMarkerResolver.Resolve(timeline),
                SelectedMarker = selectedMarker,
                ShowsGrid = showsGrid,
                ActiveHandle = itemDrag.Handle,
            };
        }

        private void OnMouseAction(Point position, D3D11Host.MouseEventKind kind, int delta)
        {
            if (timeline is null)
                return;

            var boundary = kind is not D3D11Host.MouseEventKind.Move;

            if (boundary)
            {
                RequestRender();
                SeparateHistory();
            }

            if (kind == D3D11Host.MouseEventKind.Down
                && !itemDrag.IsDragging
                && renderer.PickAxisIndicator(ToVector(position)) is { } direction)
            {
                ViewFrom(direction);
                SeparateHistory();
                return;
            }

            if (!HandleItemDrag(position, kind) && !HoldsToggleClick(position, kind))
            {
                var modifiers = Keyboard.Modifiers;

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
                    var modifiers = Keyboard.Modifiers;

                    if ((modifiers & ModifierKeys.Control) != 0)
                    {
                        toggleClickAt = position;
                        return false;
                    }

                    if ((modifiers & ModifierKeys.Alt) != 0)
                        return false;

                    return TryGrab(position);

                case D3D11Host.MouseEventKind.Move when itemDrag.IsDragging:
                    if (renderer.CreateRay(ToVector(position)) is { } ray && itemDrag.Update(ray, CurrentSnap()))
                        refresher?.ForceRefresh(timeline!);

                    return true;

                case D3D11Host.MouseEventKind.Up when itemDrag.IsDragging:
                    itemDrag.End();
                    return true;

                default:
                    return false;
            }
        }

        private bool HoldsToggleClick(Point position, D3D11Host.MouseEventKind kind)
        {
            if (toggleClickAt is not { } pressedAt)
                return false;

            switch (kind)
            {
                case D3D11Host.MouseEventKind.Move:
                    if ((position - pressedAt).Length <= ClickSlop)
                        return true;

                    toggleClickAt = null;
                    return false;

                case D3D11Host.MouseEventKind.Up:
                    toggleClickAt = null;
                    ToggleAt(position);
                    return false;

                case D3D11Host.MouseEventKind.Down:
                    return false;

                default:
                    toggleClickAt = null;
                    return false;
            }
        }

        private void ToggleAt(Point position)
        {
            renderer.RequestPick(ToVector(position));
            d3dHost?.RenderFrame();

            if (renderer.TakePickResult() is not { } picked)
                return;

            Select(selection.Contains(picked.Item)
                ? [.. selection.Where(item => item != picked.Item)]
                : [.. selection, picked.Item]);
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

                if (MovableSelection() is { Count: > 0 } movable)
                    return BeginItems(movable, movable[0], gizmo.Origin, grabbed, gizmoRay);
            }

            if (renderer.PickMarker(screen) is { } placed
                && CanGrab(placed)
                && renderer.CreateRay(screen) is { } markerRay)
            {
                Select([placed.Item]);

                return itemDrag.BeginMarker(
                    placed.Source, placed.ItemTime, placed.Marker.Position, GizmoHandle.Free, markerRay,
                    freeCamera.State.Forward, GetEditScope(placed.Item));
            }

            renderer.RequestPick(screen);
            d3dHost?.RenderFrame();

            if (renderer.TakePickResult() is not { } picked
                || renderer.CreateRay(screen) is not { } ray)
            {
                return false;
            }

            if (!selection.Contains(picked.Item))
                Select([picked.Item]);

            return MovableSelection() is { Count: > 0 } targets
                && BeginItems(targets, picked.Item, picked.Origin, GizmoHandle.Free, ray);
        }

        private IReadOnlyList<IVideoItem> MovableSelection()
            => [.. selection.Where(item => !item.IsLocked)];

        private bool BeginItems(
            IReadOnlyList<IVideoItem> items, IVideoItem primary, Vector3 origin, GizmoHandle grabbed, PickRay ray)
        {
            if (timeline is null)
                return false;

            var fps = Math.Max(1, timeline.VideoInfo.FPS);

            return itemDrag.Begin(
                [.. items.Select(item => new DragTarget(item, GetEditScope(item)))],
                primary,
                FrameContext.ForItem(primary, timeline.CurrentFrame, fps),
                origin,
                grabbed,
                ray,
                freeCamera.State.Forward);
        }

        private SnapGrid CurrentSnap()
            => new SnapGrid(snapsToGrid, snapStep, SnapGrid.DefaultAngleStep)
                .Inverted((Keyboard.Modifiers & ModifierKeys.Shift) != 0);

        private bool CanGrab(in SceneMarkerResolver.PlacedMarker marker)
            => !drivesSceneCamera || marker.Marker.Kind != MarkerKind.Camera;

        private void Select(IReadOnlyList<IItem> items)
        {
            if (timeline is null)
                return;

            timeline.SelectedItems = [.. items];
            SyncSelectionState();
        }

        public void SelectAll() => Select(renderer.VisibleItems);

        public void ClearSelection()
        {
            selection = [];
            selectedMarker = null;

            if (timeline?.SelectedItems is { Count: > 0 })
                timeline.SelectedItems = [];
        }

        private static Vector2 ToVector(Point position) => new((float)position.X, (float)position.Y);

        public bool HandleKey(Key key, ModifierKeys modifiers)
        {
            RequestRender();

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

                case Key.S when modifiers == ModifierKeys.None:
                    SnapsToGrid = !SnapsToGrid;
                    return true;

                case Key.A when modifiers == ModifierKeys.Control:
                    SelectAll();
                    return true;

                case Key.Escape when modifiers == ModifierKeys.None:
                    ClearSelection();
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
            selection = timeline?.SelectedItems is { } items ? [.. items.OfType<IVideoItem>()] : [];

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

            if (!ReferenceEquals(lastItems, timeline.Items))
            {
                lastItems = timeline.Items;
                renderer.ResetItemCaches();
            }

            sceneBuilder.UpdateItems(timeline, sourceAndDevices?.Devices);
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
