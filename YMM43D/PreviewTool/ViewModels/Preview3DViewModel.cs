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
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;
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

        private const long SettleMs = 500;

        private const long SettleIntervalMs = 33;

        private const long IdleIntervalMs = 1000;

        private readonly DisposeCollector disposer = new();
        private readonly Preview3DRenderer renderer = new();
        private readonly FreeCameraController freeCamera = new();
        private readonly ItemDragController itemDrag = new();
        private readonly BoneDragController boneDrag = new();
        private readonly OutlineDragController outlineDrag = new();
        private readonly MeshDragController meshDrag = new();
        private readonly MeshSlideController meshSlide = new();
        private readonly HashSet<int> meshSelection = [];
        private readonly PreviewSceneBuilder sceneBuilder;

        private TimelineRefresher? refresher;
        private Timeline? timeline;
        private TimelineToolInfo? toolInfo;
        private D3D11Host? d3dHost;
        private Scene? scene;
        private TimelineSourceAndDevices? sourceAndDevices;
        private IReadOnlyList<IVideoItem> selection = [];
        private IItem? selectedMarker;
        private int selectedBone = -1;
        private ImmutableList<IItem>? lastItems;
        private PreviewScene? preparedScene;
        private Point? toggleClickAt;

        private bool drivesSceneCamera;
        private bool insertsKeyFrame;
        private bool showsGrid = true;
        private bool showsMesh;
        private bool isOrthographic;
        private bool usesLocalSpace;
        private GizmoMode mode;
        private bool snapsToGrid;
        private float snapStep = SnapGrid.DefaultStep;
        private bool hadSelectedItem;
        private bool isEditingMesh;
        private MeshPart meshPart = MeshPart.Vertices;
        private long meshRevision;
        private bool slidesMesh;
        private int cutEdge = -1;
        private DoubleClick clicks;
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

        public bool ShowsMesh
        {
            get => showsMesh;
            set => Set(ref showsMesh, value, nameof(ShowsMesh));
        }

        public bool IsOrthographic
        {
            get => isOrthographic;
            set => Set(ref isOrthographic, value, nameof(IsOrthographic));
        }

        public bool UsesLocalSpace
        {
            get => usesLocalSpace;
            set => Set(ref usesLocalSpace, value, nameof(UsesLocalSpace));
        }

        public PreviewKeyText Keys { get; } = new();

        public GizmoMode Mode
        {
            get => mode;
            set
            {
                if (!Set(ref mode, value, nameof(Mode)))
                    return;

                cutEdge = -1;

                OnPropertyChanged(nameof(IsSelectingMode));
                OnPropertyChanged(nameof(IsCuttingLoops));
                OnPropertyChanged(nameof(IsInflating));
                OnPropertyChanged(nameof(IsMovingMode));
                OnPropertyChanged(nameof(IsRotatingMode));
                OnPropertyChanged(nameof(IsScalingMode));
            }
        }

        public bool IsSelectingMode
        {
            get => Mode is GizmoMode.Select;
            set => Choose(value, GizmoMode.Select);
        }

        public bool IsCuttingLoops
        {
            get => Mode is GizmoMode.LoopCut;
            set => Choose(value, GizmoMode.LoopCut);
        }

        public bool IsInflating
        {
            get => Mode is GizmoMode.Inflate;
            set => Choose(value, GizmoMode.Inflate);
        }

        public bool IsMovingMode
        {
            get => Mode is GizmoMode.Move;
            set => Choose(value, GizmoMode.Move);
        }

        public bool IsRotatingMode
        {
            get => Mode is GizmoMode.Rotate;
            set => Choose(value, GizmoMode.Rotate);
        }

        public bool IsScalingMode
        {
            get => Mode is GizmoMode.Scale;
            set => Choose(value, GizmoMode.Scale);
        }

        private void Choose(bool chosen, GizmoMode wanted)
        {
            if (chosen)
                Mode = wanted;
        }

        public bool IsEditingMesh
        {
            get => isEditingMesh;
            set => SetEditMode(value);
        }

        public bool CanEditMesh => EditableShape() is not null;

        public bool SlidesMesh
        {
            get => slidesMesh;
            set
            {
                if (slidesMesh == value)
                    return;

                slidesMesh = value && isEditingMesh;
                meshSlide.End();

                OnPropertyChanged(nameof(SlidesMesh));
                TouchHint();
                RequestRender();
            }
        }

        public string EditHint
        {
            get
            {
                if (!isEditingMesh)
                    return string.Empty;

                var part = meshPart switch
                {
                    MeshPart.Edges => "辺",
                    MeshPart.Faces => "面",
                    _ => "頂点",
                };

                var slide = slidesMesh ? "／スライド中（ドラッグで滑らせる）" : string.Empty;

                return $"編集モード：{part}を選ぶ　{meshSelection.Count} 点{slide}";
            }
        }

        public bool HasEditHint => EditHint.Length > 0;

        private void TouchHint()
        {
            OnPropertyChanged(nameof(EditHint));
            OnPropertyChanged(nameof(HasEditHint));
        }

        public bool EditsVertices
        {
            get => meshPart is MeshPart.Vertices;
            set => ChoosePart(value, MeshPart.Vertices);
        }

        public bool EditsEdges
        {
            get => meshPart is MeshPart.Edges;
            set => ChoosePart(value, MeshPart.Edges);
        }

        public bool EditsFaces
        {
            get => meshPart is MeshPart.Faces;
            set => ChoosePart(value, MeshPart.Faces);
        }

        private void ChoosePart(bool chosen, MeshPart wanted)
        {
            if (!chosen || meshPart == wanted)
                return;

            meshPart = wanted;
            meshRevision++;
            TouchHint();

            OnPropertyChanged(nameof(EditsVertices));
            OnPropertyChanged(nameof(EditsEdges));
            OnPropertyChanged(nameof(EditsFaces));

            RequestRender();
        }

        private Shape3DParameter? EditableShape()
            => selection.Count == 1
                && selection[0] is ShapeItem { ShapeParameter: Shape3DParameter found }
                && !found.IsCustom
                ? found
                : null;

        private void SetEditMode(bool wanted)
        {
            if (wanted && EditableShape() is null)
                wanted = false;

            if (isEditingMesh == wanted)
                return;

            isEditingMesh = wanted;
            meshSelection.Clear();
            meshRevision++;
            meshDrag.End();
            meshSlide.End();
            cutEdge = -1;

            if (!wanted)
            {
                slidesMesh = false;

                if (mode is GizmoMode.LoopCut or GizmoMode.Inflate)
                    Mode = GizmoMode.Move;
            }

            OnPropertyChanged(nameof(IsEditingMesh));
            OnPropertyChanged(nameof(SlidesMesh));
            TouchHint();
            RequestRender();
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

        public string SelectedBoneName => SelectedBone() is { } bone
            ? bone.HasParent ? $"ボーン {bone.Name}（親 {bone.ParentName}）" : $"ボーン {bone.Name}"
            : string.Empty;

        public bool HasSelectedBone => SelectedBoneName.Length > 0;

        private void SetSelectedBone(int index)
        {
            if (selectedBone == index)
                return;

            var left = SelectedBone();

            selectedBone = index;

            if (left is { } gone)
                gone.Parameter.DropBoneIfUntouched(gone.Name);

            if (SelectedBone() is { } chosen && !chosen.Item.IsLocked)
                chosen.Parameter.BoneFor(chosen.Name);

            OnPropertyChanged(nameof(SelectedBoneName));
            OnPropertyChanged(nameof(HasSelectedBone));
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
        public ICommand CutLoopCommand { get; }
        public ICommand ResetMeshCommand { get; }
        public ICommand ExtrudeCommand { get; }
        public ICommand DeleteMeshCommand { get; }
        public ICommand MakeFaceCommand { get; }
        public ICommand MergeMeshCommand { get; }
        public ICommand SubdivideCommand { get; }
        public ICommand SelectLinkedCommand { get; }
        public ICommand InvertSelectionCommand { get; }
        public ICommand GrowSelectionCommand { get; }
        public ICommand ShrinkSelectionCommand { get; }

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
            CutLoopCommand = new ActionCommand(_ => true, _ => CutLoop());
            ExtrudeCommand = new ActionCommand(_ => true, _ => Extrude());
            DeleteMeshCommand = new ActionCommand(_ => true, _ => DeleteMesh());
            MakeFaceCommand = new ActionCommand(_ => true, _ => MakeFace());
            MergeMeshCommand = new ActionCommand(_ => true, _ => MergeMesh());
            SubdivideCommand = new ActionCommand(_ => true, _ => Subdivide());
            SelectLinkedCommand = new ActionCommand(_ => true, _ => Reselect(mesh => mesh.LinkedTo(meshSelection)));
            InvertSelectionCommand = new ActionCommand(_ => true, _ => Reselect(Inverted));
            GrowSelectionCommand = new ActionCommand(_ => true, _ => Reselect(mesh => mesh.Grown(meshSelection)));
            ShrinkSelectionCommand = new ActionCommand(_ => true, _ => Reselect(mesh => mesh.Shrunk(meshSelection)));
            ResetMeshCommand = new ActionCommand(_ => true, _ => ResetMesh());
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

            if (active is { } aimed && aimed.Source.GetPivot(aimed.ItemTime) is { } pivot)
                freeCamera.AimAt(pivot, basis);

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
        {
            if (timeline is null)
                return EditScope.Whole;

            var time = FrameContext.ForItem(item, timeline.CurrentFrame, Math.Max(1, timeline.VideoInfo.FPS));

            return insertsKeyFrame ? EditScope.AtFrame(time.Frame) : EditScope.InSegment(time);
        }

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
                SelectedBone = selectedBone,
                ShowsGrid = showsGrid,
                ShowsMesh = showsMesh,
                IsOrthographic = isOrthographic,
                ViewDistance = freeCamera.PivotDistance,
                Space = usesLocalSpace ? GizmoSpace.Local : GizmoSpace.World,
                IsEditingMesh = isEditingMesh,
                MeshPart = meshPart,
                MeshSelection = meshSelection,
                MeshRevision = meshRevision,
                CutEdge = isEditingMesh && mode is GizmoMode.LoopCut ? cutEdge : -1,
                ShowsInflateHandle = isEditingMesh && mode is GizmoMode.Inflate,
                Mode = mode,
                ActiveHandle = meshDrag.IsDragging
                    ? meshDrag.Handle
                    : boneDrag.IsDragging ? boneDrag.Handle : itemDrag.Handle,
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

                    if (isEditingMesh && renderer.MeshEdit is { } edit)
                        return GrabMesh(edit, ToVector(position), modifiers);

                    if ((modifiers & ModifierKeys.Control) != 0)
                    {
                        toggleClickAt = position;
                        return false;
                    }

                    if ((modifiers & ModifierKeys.Alt) != 0)
                        return false;

                    return TryGrab(position);

                case D3D11Host.MouseEventKind.Move when meshSlide.IsDragging:
                    if (renderer.CreateRay(ToVector(position)) is { } slideRay && meshSlide.Update(slideRay))
                    {
                        meshRevision++;
                        refresher?.ForceRefresh(timeline!);
                    }

                    return true;

                case D3D11Host.MouseEventKind.Up when meshSlide.IsDragging:
                    meshSlide.End();
                    return true;

                case D3D11Host.MouseEventKind.Move when meshDrag.IsDragging:
                    if (renderer.CreateRay(ToVector(position)) is { } meshRay
                        && meshDrag.Update(meshRay, CurrentSnap()))
                    {
                        meshRevision++;
                        refresher?.ForceRefresh(timeline!);
                    }

                    return true;

                case D3D11Host.MouseEventKind.Up when meshDrag.IsDragging:
                    meshDrag.End();
                    return true;

                case D3D11Host.MouseEventKind.Move when outlineDrag.IsDragging:
                    if (renderer.CreateRay(ToVector(position)) is { } outlineRay
                        && outlineDrag.Update(outlineRay, CurrentSnap()))
                    {
                        refresher?.ForceRefresh(timeline!);
                    }

                    return true;

                case D3D11Host.MouseEventKind.Up when outlineDrag.IsDragging:
                    outlineDrag.End();
                    return true;

                case D3D11Host.MouseEventKind.Move when boneDrag.IsDragging:
                    if (renderer.CreateRay(ToVector(position)) is { } boneRay && boneDrag.Update(boneRay, CurrentSnap()))
                        refresher?.ForceRefresh(timeline!);

                    return true;

                case D3D11Host.MouseEventKind.Up when boneDrag.IsDragging:
                    boneDrag.End();
                    return true;

                case D3D11Host.MouseEventKind.Move when itemDrag.IsDragging:
                    if (renderer.CreateRay(ToVector(position)) is { } ray && itemDrag.Update(ray, CurrentSnap()))
                        refresher?.ForceRefresh(timeline!);

                    return true;

                case D3D11Host.MouseEventKind.Up when itemDrag.IsDragging:
                    itemDrag.End();
                    return true;

                case D3D11Host.MouseEventKind.Move when isEditingMesh && mode is GizmoMode.LoopCut:
                    HoverCut(ToVector(position));
                    return false;

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
                if (SelectedBone() is { } bone && !bone.Item.IsLocked)
                    return boneDrag.Begin(bone, gizmo, grabbed, gizmoRay, GetEditScope(bone.Item));

                if (renderer.GizmoMarker is { } held && CanGrab(held))
                {
                    return itemDrag.BeginMarker(
                        held.Source, held.ItemTime, gizmo, grabbed, gizmoRay,
                        freeCamera.State.Forward, GetEditScope(held.Item));
                }

                if (MovableSelection() is { Count: > 0 } movable)
                {
                    if (movable.Count == 1
                        && TransformGizmo.IsScale(grabbed)
                        && renderer.ResolveMeshShape() is { } stretched
                        && ReferenceEquals(stretched.Item, movable[0])
                        && meshDrag.BeginWhole(
                               stretched, gizmo, grabbed, gizmoRay, freeCamera.State.Forward))
                    {
                        return true;
                    }

                    return BeginItems(movable, movable[0], gizmo, grabbed, gizmoRay);
                }
            }

            if (renderer.Outline is { } outline
                && !outline.Item.IsLocked
                && renderer.PickOutlinePoint(screen) is var pickedPoint and >= 0
                && renderer.CreateRay(screen) is { } pointRay)
            {
                return outlineDrag.Begin(outline, pickedPoint, pointRay, GetEditScope(outline.Item));
            }

            if (renderer.PickBone(screen) is var pickedBone and >= 0)
            {
                SetSelectedBone(pickedBone);
                RequestRender();

                return true;
            }

            if (renderer.PickMarker(screen) is { } placed
                && CanGrab(placed)
                && renderer.CreateRay(screen) is { } markerRay)
            {
                Select([placed.Item]);

                return itemDrag.BeginMarker(
                    placed.Source,
                    placed.ItemTime,
                    TransformGizmo.Create(placed.Marker.Position, freeCamera.State.Position),
                    GizmoHandle.Free,
                    markerRay,
                    freeCamera.State.Forward,
                    GetEditScope(placed.Item));
            }

            renderer.RequestPick(screen);
            d3dHost?.RenderFrame();

            if (renderer.TakePickResult() is not { } picked
                || renderer.CreateRay(screen) is not { } ray)
            {
                return false;
            }

            SetSelectedBone(-1);

            if (!selection.Contains(picked.Item))
                Select([picked.Item]);

            return MovableSelection() is { Count: > 0 } targets
                && BeginItems(
                    targets,
                    picked.Item,
                    TransformGizmo.Create(picked.Origin, freeCamera.State.Position),
                    GizmoHandle.Free,
                    ray);
        }

        private void HoverCut(Vector2 screen)
        {
            var edge = renderer.MeshEdit is null ? -1 : renderer.PickMeshEdge(screen);

            if (edge == cutEdge)
                return;

            cutEdge = edge;
            RequestRender();
        }

        private bool GrabMesh(in MeshEditTarget edit, Vector2 screen, ModifierKeys modifiers)
        {
            var twice = clicks.Twice(Environment.TickCount64, screen);

            if (mode is GizmoMode.Inflate
                && renderer.InflateHandle is { } grip
                && renderer.PickInflate(screen)
                && renderer.CreateRay(screen) is { } swell
                && meshDrag.BeginInflate(edit, meshSelection, grip, swell))
            {
                return true;
            }

            if (mode is GizmoMode.LoopCut)
            {
                var wanted = renderer.PickMeshEdge(screen);

                if (wanted < 0)
                    return false;

                CutLoopAt(wanted);

                return true;
            }

            if ((modifiers & ModifierKeys.Alt) != 0 || twice)
            {
                var loop = renderer.PickMeshEdge(screen);

                if (loop < 0)
                    return false;

                ChooseMesh(edit.LoopOf(loop), (modifiers & ModifierKeys.Shift) != 0);

                return true;
            }

            if (slidesMesh
                && meshSelection.Count > 0
                && (modifiers & (ModifierKeys.Alt | ModifierKeys.Control)) == 0
                && renderer.CreateRay(screen) is { } sliding
                && meshSlide.Begin(edit, meshSelection, sliding, freeCamera.State.Forward))
            {
                return true;
            }

            if (meshSelection.Count > 0
                && renderer.PickGizmo(screen) is var grabbed and not GizmoHandle.None
                && renderer.Gizmo is { } gizmo
                && renderer.CreateRay(screen) is { } ray
                && meshDrag.Begin(edit, meshSelection, gizmo, grabbed, ray, freeCamera.State.Forward))
            {
                return true;
            }

            var extend = (modifiers & ModifierKeys.Shift) != 0;

            if ((modifiers & ModifierKeys.Control) != 0)
                return false;

            var picked = meshPart switch
            {
                MeshPart.Edges => renderer.PickMeshEdge(screen),
                MeshPart.Faces => renderer.PickMeshFace(screen),
                _ => renderer.PickMeshVertex(screen),
            };

            if (picked < 0)
                return false;

            ChooseMesh(edit.VerticesOf(meshPart, picked), extend);

            return true;
        }

        private void ChooseMesh(IEnumerable<int> vertices, bool extend)
        {
            MeshChoice.Apply(meshSelection, vertices as IReadOnlyCollection<int> ?? [.. vertices], extend);

            meshRevision++;
            TouchHint();
            RequestRender();
        }

        private void Reselect(Func<Graphics.Meshes.MeshTopology, IEnumerable<int>> work)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit)
                return;

            ChooseMesh(work(edit.Mesh), false);
        }

        private IEnumerable<int> Inverted(Graphics.Meshes.MeshTopology mesh)
            => Enumerable.Range(0, mesh.Points.Length).Where(one => !meshSelection.Contains(one));

        private void Change(
            Func<Graphics.Meshes.MeshTopology, (Graphics.Meshes.MeshTopology Mesh, int[] Chosen)?> work)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked)
                return;

            if (work(edit.Parameter.EnsureMesh()) is not { } made)
                return;

            SeparateHistory();

            edit.Parameter.StoreMesh(made.Mesh);
            meshSelection.Clear();

            foreach (var one in made.Chosen)
                meshSelection.Add(one);

            meshRevision++;

            if (timeline is not null)
                refresher?.ForceRefresh(timeline);

            TouchHint();
            RequestRender();
            SeparateHistory();
        }

        private void Extrude()
            => Change(mesh => mesh.Extrude(meshSelection) is { } made ? (made.Mesh, made.Added) : null);

        private void DeleteMesh()
            => Change(mesh => mesh.Delete(meshSelection) is { } made ? (made, []) : null);

        private void MakeFace()
            => Change(mesh => mesh.MakeFace(meshSelection) is { } made ? (made, [.. meshSelection]) : null);

        private void MergeMesh()
            => Change(mesh => mesh.Merge(meshSelection) is { } made ? (made.Mesh, made.Added) : null);

        private void Subdivide() => Change(mesh => (mesh.Subdivide(), []));

        private void CutLoop()
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked)
                return;

            for (var i = 0; i < edit.Mesh.Edges.Length; i++)
            {
                if (!edit.IsEdgeChosen(i, meshSelection))
                    continue;

                CutLoopAt(i);

                return;
            }
        }

        private void CutLoopAt(int edge)
        {
            cutEdge = -1;

            Change(mesh => mesh.CutLoop(edge) is { } made ? (made.Mesh, made.Added) : null);
        }

        private void ResetMesh()
        {
            if (EditableShape() is not { } shape)
                return;

            SeparateHistory();

            shape.DropMesh();
            meshSelection.Clear();
            meshRevision++;

            if (timeline is not null)
                refresher?.ForceRefresh(timeline);

            RequestRender();
            SeparateHistory();
        }

        private void SelectAllMesh()
        {
            if (renderer.MeshEdit is not { } edit)
                return;

            meshSelection.Clear();

            for (var i = 0; i < edit.Mesh.Points.Length; i++)
                meshSelection.Add(i);

            meshRevision++;
            RequestRender();
        }

        private PreviewBone? SelectedBone()
            => selectedBone >= 0 && selectedBone < renderer.Bones.Count ? renderer.Bones[selectedBone] : null;

        private IReadOnlyList<IVideoItem> MovableSelection()
            => [.. selection.Where(item => !item.IsLocked)];

        private bool BeginItems(
            IReadOnlyList<IVideoItem> items,
            IVideoItem primary,
            in TransformGizmo gizmo,
            GizmoHandle grabbed,
            PickRay ray)
        {
            if (timeline is null)
                return false;

            var fps = Math.Max(1, timeline.VideoInfo.FPS);

            return itemDrag.Begin(
                [.. items.Select(item => new DragTarget(
                    item, GetEditScope(item), FrameContext.ForItem(item, timeline.CurrentFrame, fps)))],
                primary,
                FrameContext.ForItem(primary, timeline.CurrentFrame, fps),
                gizmo,
                grabbed,
                ray,
                freeCamera.State.Forward,
                usesLocalSpace ? GizmoSpace.Local : GizmoSpace.World);
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

            SetSelectedBone(-1);

            timeline.SelectedItems = [.. items];
            SyncSelectionState();
        }

        public void SelectAll()
        {
            if (isEditingMesh)
            {
                SelectAllMesh();

                return;
            }

            Select(renderer.VisibleItems);
        }

        public void ClearSelection()
        {
            if (isEditingMesh)
            {
                meshSelection.Clear();
                meshRevision++;
                RequestRender();

                return;
            }

            selection = [];
            selectedMarker = null;
            SetSelectedBone(-1);

            if (timeline?.SelectedItems is { Count: > 0 })
                timeline.SelectedItems = [];
        }

        private static Vector2 ToVector(Point position) => new((float)position.X, (float)position.Y);

        public bool HandleKey(Key key, ModifierKeys modifiers)
        {
            RequestRender();

            return PreviewShortcuts.Find(key, modifiers, isEditingMesh) is { } action
                ? Run(action)
                : TryHostKey(key, modifiers);
        }

        private bool Run(PreviewAction action)
        {
            switch (action)
            {
                case PreviewAction.ViewFront:
                    ViewFrom(ViewDirection.Front);
                    return true;

                case PreviewAction.ViewBack:
                    ViewFrom(ViewDirection.Back);
                    return true;

                case PreviewAction.ViewRight:
                    ViewFrom(ViewDirection.Right);
                    return true;

                case PreviewAction.ViewLeft:
                    ViewFrom(ViewDirection.Left);
                    return true;

                case PreviewAction.ViewTop:
                    ViewFrom(ViewDirection.Top);
                    return true;

                case PreviewAction.ViewBottom:
                    ViewFrom(ViewDirection.Bottom);
                    return true;

                case PreviewAction.ToggleOrthographic:
                    IsOrthographic = !IsOrthographic;
                    return true;

                case PreviewAction.ViewCamera:
                    ResetToSceneCamera();
                    return true;

                case PreviewAction.DriveSceneCamera:
                    DrivesSceneCamera = !DrivesSceneCamera;
                    return true;

                case PreviewAction.FocusSelected:
                    FocusSelected();
                    return true;

                case PreviewAction.ViewAll:
                    ViewAll();
                    return true;

                case PreviewAction.LevelRoll:
                    LevelRoll();
                    return true;

                case PreviewAction.SelectAll:
                    SelectAll();
                    return true;

                case PreviewAction.ClearSelection:
                    ClearSelection();
                    return true;

                case PreviewAction.ToggleWireframe:
                    ShowsMesh = !ShowsMesh;
                    return true;

                case PreviewAction.ToggleGrid:
                    ShowsGrid = !ShowsGrid;
                    return true;

                case PreviewAction.ToggleSnap:
                    SnapsToGrid = !SnapsToGrid;
                    return true;

                case PreviewAction.ToggleLocalSpace:
                    UsesLocalSpace = !UsesLocalSpace;
                    return true;

                case PreviewAction.ModeSelect:
                    Mode = GizmoMode.Select;
                    return true;

                case PreviewAction.ModeLoopCut:
                    Mode = GizmoMode.LoopCut;
                    return true;

                case PreviewAction.ModeInflate:
                    Mode = GizmoMode.Inflate;
                    return true;

                case PreviewAction.ModeMove:
                    Mode = GizmoMode.Move;
                    return true;

                case PreviewAction.ModeRotate:
                    Mode = GizmoMode.Rotate;
                    return true;

                case PreviewAction.ModeScale:
                    Mode = GizmoMode.Scale;
                    return true;

                case PreviewAction.ToggleEditMode:
                    IsEditingMesh = !IsEditingMesh;
                    return true;

                case PreviewAction.EditVertices:
                    EditsVertices = true;
                    return true;

                case PreviewAction.EditEdges:
                    EditsEdges = true;
                    return true;

                case PreviewAction.EditFaces:
                    EditsFaces = true;
                    return true;

                case PreviewAction.CutLoop:
                    CutLoop();
                    return true;

                case PreviewAction.Extrude:
                    Extrude();
                    return true;

                case PreviewAction.DeleteMesh:
                    DeleteMesh();
                    return true;

                case PreviewAction.MakeFace:
                    MakeFace();
                    return true;

                case PreviewAction.MergeMesh:
                    MergeMesh();
                    return true;

                case PreviewAction.Subdivide:
                    Subdivide();
                    return true;

                case PreviewAction.ToggleSlide:
                    SlidesMesh = !SlidesMesh;
                    return true;

                case PreviewAction.SelectLinked:
                    Reselect(mesh => mesh.LinkedTo(meshSelection));
                    return true;

                case PreviewAction.InvertSelection:
                    Reselect(Inverted);
                    return true;

                case PreviewAction.GrowSelection:
                    Reselect(mesh => mesh.Grown(meshSelection));
                    return true;

                case PreviewAction.ShrinkSelection:
                    Reselect(mesh => mesh.Shrunk(meshSelection));
                    return true;

                case PreviewAction.ResetMesh:
                    ResetMesh();
                    return true;

                case PreviewAction.AddKeyFrame:
                    AddKeyFrameCommand.Execute(null);
                    return true;

                default:
                    InsertsKeyFrame = !InsertsKeyFrame;
                    return true;
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

            if (isEditingMesh && EditableShape() is null)
                SetEditMode(false);

            OnPropertyChanged(nameof(CanEditMesh));

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
