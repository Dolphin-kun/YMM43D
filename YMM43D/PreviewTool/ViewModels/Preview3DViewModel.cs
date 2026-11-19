using System.Collections.Immutable;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Project.Items;
using YMM43D.Player;
using YMM43D.PreviewTool.Rendering;
using YMM43D.PreviewTool.Views;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;
using YMM43D.Project.Shape.Modifiers;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.ItemEditor;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace YMM43D.PreviewTool.ViewModels
{
    public partial class Preview3DViewModel : Bindable, ITimelineToolViewModel, IDisposable
    {
        private const int DefaultItemLength = 300;
        private const double ClickSlop = 4;
        private const long SettleMs = 500;
        private const long SettleIntervalMs = 33;
        private const long IdleIntervalMs = 1000;
        private const float DefaultBrushRadius = 25f;
        private const float LeastBrushRadius = 4f;
        private const float BrushStep = 1.15f;
        private const float DefaultProportionalRadius = 1f;
        private const float RadiusStep = 1.15f;
        private const float InsetRatio = 0.3f;
        private const float BevelRatio = 0.2f;
        private const float SolidifyRatio = 0.5f;

        private readonly DisposeCollector disposer = new();
        private readonly Preview3DRenderer renderer = new();
        private readonly FreeCameraController freeCamera = new();
        private readonly ItemDragController itemDrag = new();
        private readonly BoneDragController boneDrag = new();
        private readonly OutlineDragController outlineDrag = new();
        private readonly MeshDragController meshDrag = new();
        private readonly MeshSlideController meshSlide = new();
        private readonly ModalTransform modal = new();
        private readonly ModalAmount amount = new();
        private readonly ConditionalWeakTable<Shape3DParameter, MeshSelection> selections = [];
        private readonly List<Vector2> lassoPoints = [];
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

        private float snapStep = SnapGrid.DefaultStep;
        private bool hadSelectedItem;
        private bool isEditingMesh;
        private MeshSelection meshSelection = new();
        private Shape3DParameter? hiddenOn;
        private long shownHidden = -1;
        private Vector2 lastMouse;
        private Vector2? regionStart;
        private bool regionDragging;
        private bool regionAdds;
        private bool regionRemoves;
        private bool boxOnce;
        private float brushRadius = DefaultBrushRadius;
        private Vector3 cursor;
        private float proportionalRadius = DefaultProportionalRadius;
        private SelectShape selectShape = SelectShape.Box;
        private PivotPoint pivot = PivotPoint.Median;
        private ProportionalFalloff falloff = ProportionalFalloff.Smooth;
        private SnapTargets snapTargets = SnapTargets.Increment;
        private SnapBase snapBase = SnapBase.Closest;
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
            get;
            set
            {
                if (!Set(ref field, value, nameof(DrivesSceneCamera)))
                    return;

                if (value)
                    EnsureSceneCamera();

                freeCamera.Invalidate();
            }
        }

        public bool InsertsKeyFrame
        {
            get;
            set => Set(ref field, value, nameof(InsertsKeyFrame));
        }

        public bool ShowsGrid
        {
            get;
            set => Set(ref field, value, nameof(ShowsGrid));
        } = true;

        public bool ShowsMesh
        {
            get;
            set => Set(ref field, value, nameof(ShowsMesh));
        }

        public bool IsOrthographic
        {
            get;
            set => Set(ref field, value, nameof(IsOrthographic));
        }

        public bool UsesLocalSpace
        {
            get;
            set => Set(ref field, value, nameof(UsesLocalSpace));
        }

        public PreviewKeyText Keys { get; } = new();

        public GizmoMode Mode
        {
            get;
            set
            {
                var changed = Set(
                    ref field,
                    value,
                    nameof(Mode),
                    nameof(IsSelectingMode),
                    nameof(IsCuttingLoops),
                    nameof(IsInflating),
                    nameof(IsMovingMode),
                    nameof(IsRotatingMode),
                    nameof(IsScalingMode));

                if (changed)
                    cutEdge = -1;
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

        private string EditHint
        {
            get
            {
                if (!isEditingMesh)
                    return string.Empty;

                if (modal.IsActive)
                    return modal.Status;

                if (amount.IsActive)
                    return amount.Status;

                if (uvEditor.IsOn)
                    return uvEditor.Status;

                if (CuttingHint is { } cutting)
                    return cutting;

                var part = meshSelection.Part switch
                {
                    MeshPart.Edges => "辺",
                    MeshPart.Faces => "面",
                    _ => "頂点",
                };

                if (boxOnce)
                    return "編集モード：範囲で選ぶ";

                if (slidesMesh)
                    return "編集モード：スライド";

                if (Mode is GizmoMode.Select)
                {
                    var shape = selectShape switch
                    {
                        SelectShape.Lasso => "投げ縄",
                        SelectShape.Circle => "円でなぞる",
                        _ => "四角",
                    };

                    return $"編集モード：{part}を選ぶ（{shape}）";
                }

                return $"編集モード：{part}を選ぶ";
            }
        }

        private void TouchHint() => RequestRender();

        public bool EditsVertices
        {
            get => meshSelection.Part is MeshPart.Vertices;
            set => ChoosePart(value, MeshPart.Vertices);
        }

        public bool EditsEdges
        {
            get => meshSelection.Part is MeshPart.Edges;
            set => ChoosePart(value, MeshPart.Edges);
        }

        public bool EditsFaces
        {
            get => meshSelection.Part is MeshPart.Faces;
            set => ChoosePart(value, MeshPart.Faces);
        }

        private void ChoosePart(bool chosen, MeshPart wanted)
        {
            if (!chosen || meshSelection.Part == wanted)
                return;

            meshSelection.SetPart(wanted);
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

            FinishModal(false);
            knife.End();
            bisectKind = null;
            bisectStart = null;
            SetRedo(null);

            isEditingMesh = wanted;
            meshRevision++;

            if (!wanted)
                ShowUv(false);

            meshDrag.End();
            meshSlide.End();
            cutEdge = -1;
            EndRegion();

            if (wanted && EditableShape() is { } shape)
            {
                meshSelection = selections.GetValue(shape, _ => new MeshSelection());
            }
            else
            {
                ReleaseHidden();
                slidesMesh = false;

                if (Mode is GizmoMode.LoopCut or GizmoMode.Inflate)
                    Mode = GizmoMode.Move;
            }

            OnPropertyChanged(nameof(IsEditingMesh));
            OnPropertyChanged(nameof(SlidesMesh));
            OnPropertyChanged(nameof(EditsVertices));
            OnPropertyChanged(nameof(EditsEdges));
            OnPropertyChanged(nameof(EditsFaces));
            TouchHint();
            RequestRender();
        }

        private void ReleaseHidden()
        {
            if (hiddenOn is { } shape)
            {
                shape.PreviewHidden = null;
                shape.IsEditing = false;
            }

            hiddenOn = null;
            shownHidden = -1;
        }

        public bool IsXRay
        {
            get;
            set
            {
                if (field == value)
                    return;

                field = value;
                OnPropertyChanged(nameof(IsXRay));
                TouchHint();
            }
        }

        public bool SelectsByBox
        {
            get => selectShape is SelectShape.Box;
            set => ChooseShape(value, SelectShape.Box);
        }

        public bool SelectsByLasso
        {
            get => selectShape is SelectShape.Lasso;
            set => ChooseShape(value, SelectShape.Lasso);
        }

        public bool SelectsByCircle
        {
            get => selectShape is SelectShape.Circle;
            set => ChooseShape(value, SelectShape.Circle);
        }

        private void ChooseShape(bool chosen, SelectShape wanted)
        {
            if (!chosen || selectShape == wanted)
                return;

            selectShape = wanted;
            OnPropertyChanged(nameof(SelectsByBox));
            OnPropertyChanged(nameof(SelectsByLasso));
            OnPropertyChanged(nameof(SelectsByCircle));
        }

        public bool PivotsAtMedian
        {
            get => pivot is PivotPoint.Median;
            set => ChoosePivot(value, PivotPoint.Median);
        }

        public bool PivotsAtBounds
        {
            get => pivot is PivotPoint.Bounds;
            set => ChoosePivot(value, PivotPoint.Bounds);
        }

        public bool PivotsAtCursor
        {
            get => pivot is PivotPoint.Cursor;
            set => ChoosePivot(value, PivotPoint.Cursor);
        }

        public bool PivotsIndividually
        {
            get => pivot is PivotPoint.Individual;
            set => ChoosePivot(value, PivotPoint.Individual);
        }

        public bool PivotsAtActive
        {
            get => pivot is PivotPoint.Active;
            set => ChoosePivot(value, PivotPoint.Active);
        }

        private void ChoosePivot(bool chosen, PivotPoint wanted)
        {
            if (!chosen || pivot == wanted)
                return;

            pivot = wanted;
            OnPropertyChanged(nameof(PivotsAtMedian));
            OnPropertyChanged(nameof(PivotsAtBounds));
            OnPropertyChanged(nameof(PivotsAtCursor));
            OnPropertyChanged(nameof(PivotsIndividually));
            OnPropertyChanged(nameof(PivotsAtActive));
        }

        public bool IsProportional
        {
            get;
            set
            {
                if (field == value)
                    return;

                field = value;
                OnPropertyChanged(nameof(IsProportional));
                TouchHint();
            }
        }

        public bool ConnectsProportional
        {
            get;
            set
            {
                if (field == value)
                    return;

                field = value;
                OnPropertyChanged(nameof(ConnectsProportional));
            }
        }

        public bool FallsSmooth
        {
            get => falloff is ProportionalFalloff.Smooth;
            set => ChooseFalloff(value, ProportionalFalloff.Smooth);
        }

        public bool FallsSphere
        {
            get => falloff is ProportionalFalloff.Sphere;
            set => ChooseFalloff(value, ProportionalFalloff.Sphere);
        }

        public bool FallsRoot
        {
            get => falloff is ProportionalFalloff.Root;
            set => ChooseFalloff(value, ProportionalFalloff.Root);
        }

        public bool FallsInverseSquare
        {
            get => falloff is ProportionalFalloff.InverseSquare;
            set => ChooseFalloff(value, ProportionalFalloff.InverseSquare);
        }

        public bool FallsSharp
        {
            get => falloff is ProportionalFalloff.Sharp;
            set => ChooseFalloff(value, ProportionalFalloff.Sharp);
        }

        public bool FallsLinear
        {
            get => falloff is ProportionalFalloff.Linear;
            set => ChooseFalloff(value, ProportionalFalloff.Linear);
        }

        public bool FallsConstant
        {
            get => falloff is ProportionalFalloff.Constant;
            set => ChooseFalloff(value, ProportionalFalloff.Constant);
        }

        public bool FallsRandom
        {
            get => falloff is ProportionalFalloff.Random;
            set => ChooseFalloff(value, ProportionalFalloff.Random);
        }

        private void ChooseFalloff(bool chosen, ProportionalFalloff wanted)
        {
            if (!chosen || falloff == wanted)
                return;

            falloff = wanted;
            OnPropertyChanged(nameof(FallsSmooth));
            OnPropertyChanged(nameof(FallsSphere));
            OnPropertyChanged(nameof(FallsRoot));
            OnPropertyChanged(nameof(FallsInverseSquare));
            OnPropertyChanged(nameof(FallsSharp));
            OnPropertyChanged(nameof(FallsLinear));
            OnPropertyChanged(nameof(FallsConstant));
            OnPropertyChanged(nameof(FallsRandom));
        }

        public bool SnapsToIncrement
        {
            get => (snapTargets & SnapTargets.Increment) != 0;
            set => ToggleTarget(value, SnapTargets.Increment);
        }

        public bool SnapsToVertex
        {
            get => (snapTargets & SnapTargets.Vertex) != 0;
            set => ToggleTarget(value, SnapTargets.Vertex);
        }

        public bool SnapsToEdge
        {
            get => (snapTargets & SnapTargets.Edge) != 0;
            set => ToggleTarget(value, SnapTargets.Edge);
        }

        public bool SnapsToFace
        {
            get => (snapTargets & SnapTargets.Face) != 0;
            set => ToggleTarget(value, SnapTargets.Face);
        }

        private void ToggleTarget(bool wanted, SnapTargets target)
        {
            snapTargets = wanted ? snapTargets | target : snapTargets & ~target;

            OnPropertyChanged(nameof(SnapsToIncrement));
            OnPropertyChanged(nameof(SnapsToVertex));
            OnPropertyChanged(nameof(SnapsToEdge));
            OnPropertyChanged(nameof(SnapsToFace));
        }

        public bool SnapsFromCenter
        {
            get => snapBase is SnapBase.Center;
            set => ChooseBase(value, SnapBase.Center);
        }

        public bool SnapsFromClosest
        {
            get => snapBase is SnapBase.Closest;
            set => ChooseBase(value, SnapBase.Closest);
        }

        public bool SnapsFromActive
        {
            get => snapBase is SnapBase.Active;
            set => ChooseBase(value, SnapBase.Active);
        }

        private void ChooseBase(bool chosen, SnapBase wanted)
        {
            if (!chosen || snapBase == wanted)
                return;

            snapBase = wanted;
            OnPropertyChanged(nameof(SnapsFromCenter));
            OnPropertyChanged(nameof(SnapsFromClosest));
            OnPropertyChanged(nameof(SnapsFromActive));
        }

        public bool SnapsToGrid
        {
            get;
            set => Set(ref field, value, nameof(SnapsToGrid));
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

        private string SelectedBoneName => SelectedBone() is { } bone
            ? bone.HasParent ? $"ボーン {bone.Name}（親 {bone.ParentName}）" : $"ボーン {bone.Name}"
            : string.Empty;

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

            RequestRender();
        }

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
        public ICommand MeshCommand { get; }
        public MeshMenuItems MeshMenus { get; }
        public ICommand JoinCommand { get; }
        public ICommand InsetCommand { get; }
        public ICommand BevelCommand { get; }
        public ICommand SolidifyCommand { get; }
        public ICommand MirrorCommand { get; }
        public ICommand SelectLinkedCommand { get; }
        public ICommand InvertSelectionCommand { get; }
        public ICommand GrowSelectionCommand { get; }
        public ICommand ShrinkSelectionCommand { get; }
        public ICommand BoxSelectCommand { get; }
        public ICommand HideCommand { get; }
        public ICommand HideOthersCommand { get; }
        public ICommand RevealCommand { get; }
        public ICommand CursorToSelectedCommand { get; }
        public ICommand CursorToOriginCommand { get; }
        public ICommand SelectionToCursorCommand { get; }

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
            MeshCommand = meshCommand = new ActionCommand(CanRunMesh, RunMesh);
            MeshMenus = new MeshMenuItems(MeshCommand);
            WireSidebar();
            JoinCommand = new ActionCommand(_ => true, _ => Join());
            InsetCommand = new ActionCommand(_ => true, _ => Inset());
            BevelCommand = new ActionCommand(_ => true, _ => Bevel());
            SolidifyCommand = new ActionCommand(_ => true, _ => Solidify());
            MirrorCommand = new ActionCommand(_ => true, _ => MirrorMesh());
            SelectLinkedCommand = new ActionCommand(_ => true, _ => Reselect(mesh => mesh.LinkedTo(meshSelection.Vertices)));
            InvertSelectionCommand = new ActionCommand(_ => true, _ => InvertMesh());
            GrowSelectionCommand = new ActionCommand(_ => true, _ => Reselect(mesh => mesh.Grown(meshSelection.Vertices)));
            ShrinkSelectionCommand = new ActionCommand(_ => true, _ => Reselect(mesh => mesh.Shrunk(meshSelection.Vertices)));
            BoxSelectCommand = new ActionCommand(_ => true, _ => StartBoxOnce());
            HideCommand = new ActionCommand(_ => true, _ => HideMesh(false));
            HideOthersCommand = new ActionCommand(_ => true, _ => HideMesh(true));
            RevealCommand = new ActionCommand(_ => true, _ => RevealMesh());
            CursorToSelectedCommand = new ActionCommand(_ => true, _ => CursorToSelected());
            CursorToOriginCommand = new ActionCommand(_ => true, _ => CursorToOrigin());
            SelectionToCursorCommand = new ActionCommand(_ => true, _ => SelectionToCursor());
            ResetMeshCommand = new ActionCommand(_ => true, _ => ResetMesh());
            FocusSelectedCommand = new ActionCommand(_ => true, _ => FocusSelected());
            ViewAllCommand = new ActionCommand(_ => true, _ => ViewAll());
            LevelRollCommand = new ActionCommand(_ => true, _ => LevelRoll());
            ViewFromCommand = new ActionCommand(_ => true, p => ViewFrom(p as string));
            SelectAllCommand = new ActionCommand(_ => true, _ => SelectAll());
            ClearSelectionCommand = new ActionCommand(_ => true, _ => ClearSelection());
            AddKeyFrameCommand = new ActionCommand(_ => HasSelectedItem, _ => HostCommands.Execute(CommandType.AddKeyFrameAtCurrentFrame, d3dHost));
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
                    sourceAndDevices = disposer.CollectEx(new TimelineSourceAndDevices(scene));
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

            var active = DrivesSceneCamera ? SceneCameraResolver.Find(timeline) : null;
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

            return InsertsKeyFrame ? EditScope.AtFrame(time.Frame) : EditScope.InSegment(time);
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

        private CameraState ResolveCamera() => timeline is null ? CameraState.Default : SceneCameraResolver.Resolve(timeline);

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
            if (d3dHost is not null && HoldsMouse)
                d3dHost.PollMouse();

            if (d3dHost is null || timeline is null || !ShouldRender(d3dHost))
                return;

            renderRequested = false;
            lastRenderAt = Environment.TickCount64;

            using (SceneRevision.Mute())
            {
                if (DrivesSceneCamera && SceneCameraResolver.Find(timeline) is not null)
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

            renderer.Draw(
                device, context, renderTarget, depthStencil, width, height, d3dHost.PixelScale, prepared, d3dHost.DepthView);

            d3dHost.UvSheet = UvSheet(width, height);

            d3dHost.Labels = Labels();

            if (meshSelection.Revision != menuRevision)
            {
                menuRevision = meshSelection.Revision;
                meshCommand.RaiseCanExecuteChanged();
            }
            d3dHost.SidebarOpen = IsSidebarOpen;
            ShowSidebar();

            if (redo is { IsCurrent: false })
                SetRedo(null);
        }

        private PreviewScene? CreatePreviewScene(ID3D11Device device)
        {
            if (timeline is null || sourceAndDevices is null)
                return null;

            SyncMeshEdit();

            var time = TimelineRefresher.GetTime(timeline);
            var camera = ResolveCamera();
            freeCamera.EnsureInitialized(camera);

            return new PreviewScene
            {
                ViewPose = freeCamera.GetPose(),
                SceneCamera = camera,
                Lighting = SceneLightingResolver.Resolve(timeline),
                Time = time,
                Environment = new PreviewEnvironment(device, sourceAndDevices.Devices, scene, sceneBuilder.SourceDescription),
                Items = sceneBuilder.Items,
                Selection = selection,
                Markers = SceneMarkerResolver.Resolve(timeline),
                SelectedMarker = selectedMarker,
                SelectedBone = selectedBone,
                ShowsGrid = ShowsGrid,
                ShowsMesh = ShowsMesh,
                IsOrthographic = IsOrthographic,
                ViewDistance = freeCamera.PivotDistance,
                Space = UsesLocalSpace ? GizmoSpace.Local : GizmoSpace.World,
                IsEditingMesh = isEditingMesh,
                MeshSelection = meshSelection,
                XRay = IsXRay,
                Pivot = pivot,
                Cursor = cursor,
                Overlay = isEditingMesh ? BuildOverlay() : null,
                MeshRevision = meshRevision,
                CutEdge = isEditingMesh && Mode is GizmoMode.LoopCut ? cutEdge : -1,
                ShowsInflateHandle = isEditingMesh && Mode is GizmoMode.Inflate,
                Mode = Mode,
                ActiveHandle = meshDrag.IsDragging
                    ? meshDrag.Handle
                    : boneDrag.IsDragging ? boneDrag.Handle : itemDrag.Handle,
            };
        }

        private void OnMouseAction(Point position, D3D11Host.MouseEventKind kind, int delta)
        {
            if (timeline is null)
                return;

            if (kind is not D3D11Host.MouseEventKind.Wheel)
                lastMouse = ToVector(position);

            if (ClicksSidebarTab(kind))
            {
                RequestRender();
                return;
            }

            if (UsesUvMouse && renderer.MeshEdit is { } uvTarget)
            {
                uvEditor.Mouse(kind, lastMouse, delta, Keyboard.Modifiers, uvTarget.Mesh, meshSelection);
                TouchHint();
                RequestRender();
                return;
            }

            if (isEditingMesh && HandleEditMouse(kind, delta))
            {
                RequestRender();
                return;
            }

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
                        && meshDrag.Update(meshRay, ToVector(position), renderer.View, CurrentSnapSettings()))
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
                    if (renderer.CreateRay(ToVector(position)) is { } boneRay
                        && boneDrag.Update(boneRay, CurrentSnap()))
                    {
                        refresher?.ForceRefresh(timeline!);
                    }

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

                case D3D11Host.MouseEventKind.Move when isEditingMesh && Mode is GizmoMode.LoopCut:
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

                if (renderer.Handles is { } grabbedHandles && !grabbedHandles.Item.IsLocked)
                {
                    return itemDrag.BeginMarker(
                        new HandleMarker(grabbedHandles),
                        grabbedHandles.Time,
                        gizmo,
                        grabbed,
                        gizmoRay,
                        freeCamera.State.Forward,
                        GetEditScope(grabbedHandles.Item));
                }

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

            if (renderer.Handles is { } pickedHandles && renderer.PickHandle(screen) is var pickedPointIndex and >= 0)
            {
                pickedHandles.Source.SelectedHandle = pickedPointIndex;
                RequestRender();

                return true;
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

            if (renderer.TakePickResult() is not { } picked || renderer.CreateRay(screen) is not { } ray)
                return false;

            SetSelectedBone(-1);

            if (!selection.Contains(picked.Item))
                Select([picked.Item]);

            return MovableSelection() is { Count: > 0 } targets
                && BeginItems(targets, picked.Item, TransformGizmo.Create(picked.Origin, freeCamera.State.Position), GizmoHandle.Free, ray);
        }

        private void HoverCut(Vector2 screen)
        {
            var edge = renderer.MeshPick?.Edge(screen, IsXRay) ?? -1;

            if (edge == cutEdge)
                return;

            cutEdge = edge;
            RequestRender();
        }

        private bool GrabMesh(in MeshEditTarget edit, Vector2 screen, ModifierKeys modifiers)
        {
            var twice = clicks.Twice(Environment.TickCount64, screen);
            var pick = renderer.MeshPick;

            if (Mode is GizmoMode.Inflate
                && renderer.InflateHandle is { } grip
                && renderer.PickInflate(screen)
                && renderer.CreateRay(screen) is { } swell
                && meshDrag.BeginInflate(edit, meshSelection, grip, swell, Proportional()))
            {
                return true;
            }

            if (Mode is GizmoMode.LoopCut)
            {
                var wanted = pick?.Edge(screen, IsXRay) ?? -1;

                if (wanted < 0)
                    return false;

                CutLoopAt(wanted);

                return true;
            }

            if ((modifiers & ModifierKeys.Alt) != 0 || twice)
            {
                var loop = pick?.Edge(screen, IsXRay) ?? -1;

                if (loop < 0)
                    return false;

                ChooseMesh(edit.LoopOf(meshSelection.Part, loop), (modifiers & ModifierKeys.Shift) != 0 ? SelectOp.Add : SelectOp.Set);

                return true;
            }

            if (slidesMesh
                && !meshSelection.IsEmpty
                && (modifiers & (ModifierKeys.Alt | ModifierKeys.Control)) == 0
                && renderer.CreateRay(screen) is { } sliding
                && meshSlide.Begin(edit, meshSelection.Vertices, sliding, freeCamera.State.Forward))
            {
                return true;
            }

            if (!meshSelection.IsEmpty
                && renderer.PickGizmo(screen) is var grabbed and not GizmoHandle.None
                && renderer.Gizmo is { } gizmo
                && renderer.CreateRay(screen) is { } ray
                && meshDrag.Begin(edit, meshSelection, gizmo, grabbed, ray, freeCamera.State.Forward, pivot, cursor, Proportional(), Snapper(edit)))
            {
                return true;
            }

            if (boxOnce || Mode is GizmoMode.Select)
            {
                StartRegion(screen, modifiers);

                return true;
            }

            if ((modifiers & ModifierKeys.Control) != 0)
                return false;

            var picked = PickElement(screen);

            if (picked < 0)
                return false;

            ChooseMesh([picked], (modifiers & ModifierKeys.Shift) != 0 ? SelectOp.Toggle : SelectOp.Set, picked);

            return true;
        }

        private int PickElement(Vector2 screen) => renderer.MeshPick is not { } pick
            ? -1
            : meshSelection.Part switch
            {
                MeshPart.Edges => pick.Edge(screen, IsXRay),
                MeshPart.Faces => pick.Face(screen, IsXRay),
                _ => pick.Vertex(screen, IsXRay),
            };

        private void ChooseMesh(IEnumerable<int> elements, SelectOp op, int active = -1)
        {
            meshSelection.Apply(elements, op, active);

            TouchHint();
            RequestRender();
        }

        private void Reselect(Func<MeshTopology, IEnumerable<int>> work)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit)
                return;

            meshSelection.Bind(edit.Mesh);
            meshSelection.SelectVertices(work(edit.Mesh));

            TouchHint();
            RequestRender();
        }

        private void InvertMesh()
        {
            if (!isEditingMesh)
                return;

            meshSelection.Invert();

            TouchHint();
            RequestRender();
        }

        private bool HandleEditMouse(D3D11Host.MouseEventKind kind, int delta)
        {
            if (IsCutting)
                return HandleCuttingMouse(kind);

            if (modal.IsActive || amount.IsActive)
            {
                switch (kind)
                {
                    case D3D11Host.MouseEventKind.Move:
                        UpdateModal();
                        break;

                    case D3D11Host.MouseEventKind.Down:
                        FinishModal(true);
                        break;

                    case D3D11Host.MouseEventKind.RightDown:
                        FinishModal(false);
                        break;

                    case D3D11Host.MouseEventKind.Wheel when modal.IsActive:
                        modal.Widen(delta > 0 ? RadiusStep : 1f / RadiusStep);
                        KeepRadius(modal.Proportional);
                        UpdateModal();
                        break;

                    case D3D11Host.MouseEventKind.Wheel:
                        amount.Step(delta > 0 ? 1 : -1, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
                        UpdateModal();
                        break;
                }

                return true;
            }

            if (meshDrag.IsDragging && kind is D3D11Host.MouseEventKind.Wheel)
            {
                meshDrag.Widen(delta > 0 ? RadiusStep : 1f / RadiusStep);
                KeepRadius(meshDrag.Proportional);

                return true;
            }

            if (regionStart is { } start)
            {
                switch (kind)
                {
                    case D3D11Host.MouseEventKind.Move:
                        if (!regionDragging && Vector2.Distance(start, lastMouse) > ClickSlop)
                            regionDragging = true;

                        if (regionDragging && selectShape is SelectShape.Lasso)
                            lassoPoints.Add(lastMouse);

                        if (regionDragging && selectShape is SelectShape.Circle)
                            Paint();

                        break;

                    case D3D11Host.MouseEventKind.Up:
                        FinishRegion();
                        break;

                    case D3D11Host.MouseEventKind.Wheel when selectShape is SelectShape.Circle:
                        brushRadius = MathF.Max(brushRadius * (delta > 0 ? BrushStep : 1f / BrushStep), LeastBrushRadius);
                        break;

                    case D3D11Host.MouseEventKind.RightDown:
                        EndRegion();
                        break;
                }

                return true;
            }

            if (kind is D3D11Host.MouseEventKind.RightDown && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                PlaceCursor(lastMouse);

                return true;
            }

            if (kind is D3D11Host.MouseEventKind.Move && UsesBrush)
                RequestRender();

            return false;
        }

        private bool UsesBrush => isEditingMesh && Mode is GizmoMode.Select && selectShape is SelectShape.Circle;

        private void StartRegion(Vector2 screen, ModifierKeys modifiers)
        {
            regionStart = screen;
            regionDragging = selectShape is SelectShape.Circle && !boxOnce;
            regionAdds = (modifiers & ModifierKeys.Shift) != 0;
            regionRemoves = (modifiers & ModifierKeys.Control) != 0;
            lassoPoints.Clear();
            lassoPoints.Add(screen);

            if (regionDragging)
                Paint();
        }

        private SelectShape RegionShape => boxOnce ? SelectShape.Box : selectShape;

        private ScreenRegion? CurrentRegion()
        {
            if (regionStart is not { } start)
                return null;

            return RegionShape switch
            {
                SelectShape.Lasso => ScreenRegion.Lasso(lassoPoints),
                SelectShape.Circle => ScreenRegion.Circle(lastMouse, brushRadius),
                _ => ScreenRegion.Box(start, lastMouse),
            };
        }

        private void Paint()
        {
            if (renderer.MeshPick is not { } pick)
                return;

            var found = pick.Region(meshSelection.Part, ScreenRegion.Circle(lastMouse, brushRadius), IsXRay);

            if (found.Length > 0)
                ChooseMesh(found, regionRemoves ? SelectOp.Subtract : SelectOp.Add);
        }

        private void FinishRegion()
        {
            if (regionStart is not { } start)
                return;

            if (RegionShape is not SelectShape.Circle)
            {
                if (!regionDragging)
                {
                    var picked = PickElement(start);

                    if (picked >= 0)
                        ChooseMesh([picked], regionAdds ? SelectOp.Toggle : regionRemoves ? SelectOp.Subtract : SelectOp.Set, picked);
                    else if (!regionAdds && !regionRemoves)
                        ChooseMesh([], SelectOp.Set);
                }
                else if (CurrentRegion() is { IsUsable: true } region && renderer.MeshPick is { } pick)
                {
                    ChooseMesh(pick.Region(meshSelection.Part, region, IsXRay), regionAdds ? SelectOp.Add : regionRemoves ? SelectOp.Subtract : SelectOp.Set);
                }
            }

            EndRegion();
        }

        private void EndRegion()
        {
            regionStart = null;
            regionDragging = false;
            boxOnce = false;
            lassoPoints.Clear();
            TouchHint();
            RequestRender();
        }

        private void StartBoxOnce()
        {
            if (!isEditingMesh)
                return;

            boxOnce = true;
            TouchHint();
        }

        private ScreenOverlay BuildOverlay()
        {
            ScreenRegion? region = null;

            if (regionDragging || (regionStart is not null && RegionShape is SelectShape.Circle))
                region = CurrentRegion();
            else if (UsesBrush && !modal.IsActive && !amount.IsActive)
                region = ScreenRegion.Circle(lastMouse, brushRadius);

            var guided = modal.IsActive && modal.Kind is not TransformKind.Move;
            var (lines, marks) = CuttingOverlay();

            return new ScreenOverlay
            {
                Region = region,
                GuideFrom = guided ? modal.PivotScreen : amount.ShowsGuide ? amount.PivotScreen : null,
                GuideTo = guided ? modal.Mouse : amount.ShowsGuide ? amount.Mouse : null,
                AxisOrigin = modal.Pivot,
                Axes = modal.IsActive ? [.. modal.Guides()] : [],
                Cursor = cursor,
                Lines = lines,
                Marks = marks,
            };
        }

        private void SyncMeshEdit()
        {
            if (!isEditingMesh || EditableShape() is not { } shape)
                return;

            shape.IsEditing = true;
            meshSelection.Bind(shape.ShownMesh());

            if (ReferenceEquals(shape, hiddenOn) && shownHidden == meshSelection.HiddenRevision)
                return;

            if (hiddenOn is { } before && !ReferenceEquals(before, shape))
            {
                before.PreviewHidden = null;
                before.IsEditing = false;
            }

            hiddenOn = shape;
            shownHidden = meshSelection.HiddenRevision;
            shape.PreviewHidden = meshSelection.HiddenFaces.Count > 0 ? new HashSet<int>(meshSelection.HiddenFaces) : null;
        }

        private ProportionalEdit? Proportional() => IsProportional ? new ProportionalEdit(proportionalRadius, falloff, ConnectsProportional) : null;

        private void KeepRadius(ProportionalEdit? shaped)
        {
            if (shaped is { } known)
                proportionalRadius = known.Radius;

            TouchHint();
            RequestRender();
        }

        private MeshSnapper? Snapper(in MeshEditTarget edit)
            => (snapTargets & (SnapTargets.Vertex | SnapTargets.Edge | SnapTargets.Face)) != 0
                ? new MeshSnapper(edit, meshSelection, renderer.OtherSurfaces(edit.Item))
                : null;

        private SnapSettings CurrentSnapSettings()
            => new(SnapsToGrid != ((Keyboard.Modifiers & ModifierKeys.Shift) != 0), snapTargets, snapBase, new SnapGrid(true, snapStep, SnapGrid.DefaultAngleStep));

        private void BeginTransform(TransformKind kind, Vector3? along = null)
        {
            if (!isEditingMesh || renderer.ResolveMeshShape() is not { } edit || meshSelection.IsEmpty)
                return;

            SeparateHistory();

            if (!modal.Begin(kind, edit, meshSelection, pivot, cursor, Proportional(), Snapper(edit), lastMouse, renderer.View))
                return;

            if (along is { } direction)
                modal.LockTo(direction);

            UpdateModal();
        }

        private void UpdateModal()
        {
            if (modal.IsActive)
                modal.Update(lastMouse, renderer.View, CurrentSnapSettings());
            else if (amount.IsActive)
                amount.Update(lastMouse, CurrentSnapSettings());
            else
                return;

            Refresh();
        }

        private void FinishModal(bool confirm)
        {
            if (!modal.IsActive && !amount.IsActive)
                return;

            if (modal.IsActive)
            {
                if (confirm)
                    modal.Confirm();
                else
                    modal.Cancel();
            }

            if (amount.IsActive)
            {
                if (confirm)
                    amount.Confirm();
                else
                    amount.Cancel();
            }

            Refresh();
            SeparateHistory();
            KeepRedo();
        }

        private void Refresh()
        {
            meshRevision++;

            if (timeline is not null)
                refresher?.ForceRefresh(timeline);

            TouchHint();
            RequestRender();
        }

        private bool HandleModalKey(Key key, ModifierKeys modifiers)
        {
            var result = modal.IsActive ? modal.Press(key, modifiers) : amount.Press(key);

            switch (result)
            {
                case ModalResult.Confirmed or ModalResult.Cancelled:
                    Refresh();
                    SeparateHistory();
                    KeepRedo();
                    break;

                case ModalResult.ToggleProportional:
                    IsProportional = !IsProportional;
                    modal.SetProportional(Proportional());
                    UpdateModal();
                    break;

                case ModalResult.Restart:
                    var next = modal.Requested;

                    modal.Cancel();
                    BeginTransform(next);
                    break;

                default:
                    UpdateModal();
                    break;
            }

            return true;
        }

        private void Change(Func<MeshTopology, (MeshTopology Mesh, int[] Chosen)?> work, Action<Shape3DParameter>? also = null)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked)
                return;

            var mesh = edit.Parameter.EnsureMesh();

            meshSelection.Bind(mesh);

            if (work(mesh) is not { } made)
                return;

            SeparateHistory();

            meshSelection.Replace(edit.Parameter.StoreMesh(made.Mesh.Carry(mesh)), made.Chosen);
            also?.Invoke(edit.Parameter);

            Refresh();
            SeparateHistory();
        }

        private Vector3 Normal(in MeshEditTarget edit)
        {
            var sum = Vector3.Zero;

            foreach (var face in meshSelection.Faces)
            {
                if (face < edit.Mesh.Faces.Length)
                    sum += Vector3.TransformNormal(edit.Mesh.NormalOf(face), edit.ToWorld);
            }

            return sum;
        }

        private void Inset() => BeginAmount(edit => new AmountSpec("差し込み", AmountUnit.Length, Step(edit.Mesh) * InsetRatio), (mesh, chosen, value) => mesh.Inset(chosen.Faces, value.Amount));

        private void Bevel() => BeginAmount(edit => new AmountSpec("ベベル", AmountUnit.Length, Step(edit.Mesh) * BevelRatio), (mesh, chosen, value) => mesh.Bevel(chosen.Faces, value.Amount));

        private void Solidify() => BeginAmount(edit => new AmountSpec("厚み", AmountUnit.Length, Step(edit.Mesh) * SolidifyRatio, true), (mesh, chosen, value) => mesh.Solidify(chosen.Faces, value.Amount));

        private void MirrorMesh()
        {
            if (EditableShape() is not { } shape)
                return;

            SeparateHistory();
            shape.Modifiers = shape.Modifiers.Add(new MirrorModifier());
            Refresh();
            SeparateHistory();
        }

        private static float Step(MeshTopology mesh)
        {
            if (mesh.Edges.Length == 0)
                return 0f;

            var shortest = float.MaxValue;

            foreach (var edge in mesh.Edges)
            {
                var span = (mesh.Points[edge.A] - mesh.Points[edge.B]).Length();

                if (span > 1e-5f)
                    shortest = MathF.Min(shortest, span);
            }

            return shortest < float.MaxValue ? shortest : 0f;
        }

        private void CutLoop()
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked)
                return;

            meshSelection.Bind(edit.Mesh);

            foreach (var edge in meshSelection.Edges)
            {
                CutLoopAt(edge);

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
            meshSelection.Reveal();
            meshSelection.Clear();
            ReleaseHidden();

            Refresh();
            SeparateHistory();
        }

        private void SelectAllMesh()
        {
            meshSelection.SelectAll();

            TouchHint();
            RequestRender();
        }

        private void HideMesh(bool others)
        {
            if (!isEditingMesh)
                return;

            meshSelection.Hide(others);

            TouchHint();
            RequestRender();
        }

        private void RevealMesh()
        {
            if (!isEditingMesh)
                return;

            meshSelection.Reveal();

            TouchHint();
            RequestRender();
        }

        private void PlaceCursor(Vector2 screen)
        {
            if (renderer.MeshPick?.Surface(screen) is { } hit)
            {
                JumpCursor(hit);
            }
            else if (renderer.CreateRay(screen) is { } ray
                && ray.IntersectPlane(cursor, -renderer.View.Forward) is { } across)
            {
                JumpCursor(across);
            }
        }

        private void CursorToSelected()
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || meshSelection.IsEmpty)
                return;

            JumpCursor(MeshPivot.Median(edit.Mesh, edit.ToWorld, meshSelection.Vertices));
        }

        private void CursorToOrigin() => JumpCursor(Vector3.Zero);

        private void JumpCursor(Vector3 to)
        {
            SeparateHistory();
            MoveCursor(to);
            SeparateHistory();
        }

        private void MoveCursor(Vector3 to)
        {
            if (to == cursor)
                return;

            toolInfo?.UndoRedoManager?.AddCommand(new CursorCommand(cursor, to, SetCursor));
            SetCursor(to);
        }

        private void SetCursor(Vector3 to)
        {
            cursor = to;
            RequestRender();
        }

        private void SelectionToCursor()
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked || meshSelection.IsEmpty)
                return;

            SeparateHistory();
            MoveSelectionTo(cursor);
            SeparateHistory();
        }

        private void MoveSelectionTo(Vector3 target)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked || meshSelection.IsEmpty)
                return;

            var mesh = edit.Parameter.EnsureMesh();

            meshSelection.Bind(mesh);

            var mover = new MeshTransformer(mesh, edit.ToWorld, meshSelection, PivotPoint.Median, cursor, null);

            edit.Parameter.StorePoints(mesh.WithPoints(mover.Translate(target - mover.Pivot)));
            Refresh();
        }

        private void CyclePivot()
        {
            var next = (PivotPoint)(((int)pivot + 1) % Enum.GetValues<PivotPoint>().Length);

            ChoosePivot(true, next);
        }

        private void CycleFalloff()
        {
            var next = (ProportionalFalloff)(((int)falloff + 1) % Enum.GetValues<ProportionalFalloff>().Length);

            ChooseFalloff(true, next);
        }

        private void CycleSelectShape()
        {
            var next = (SelectShape)(((int)selectShape + 1) % Enum.GetValues<SelectShape>().Length);

            ChooseShape(true, next);
        }

        private PreviewBone? SelectedBone() => selectedBone >= 0 && selectedBone < renderer.Bones.Count ? renderer.Bones[selectedBone] : null;

        private IReadOnlyList<IVideoItem> MovableSelection() => [.. selection.Where(item => !item.IsLocked)];

        private bool BeginItems(IReadOnlyList<IVideoItem> items, IVideoItem primary, in TransformGizmo gizmo, GizmoHandle grabbed, PickRay ray)
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
                UsesLocalSpace ? GizmoSpace.Local : GizmoSpace.World);
        }

        private SnapGrid CurrentSnap()
            => new SnapGrid(SnapsToGrid, snapStep, SnapGrid.DefaultAngleStep)
                .Inverted((Keyboard.Modifiers & ModifierKeys.Shift) != 0);

        private bool CanGrab(in SceneMarkerResolver.PlacedMarker marker) => !DrivesSceneCamera || marker.Marker.Kind != MarkerKind.Camera;

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
                TouchHint();
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

            if (isEditingMesh && (modal.IsActive || amount.IsActive))
                return HandleModalKey(key, modifiers);

            if (isEditingMesh && IsCutting)
                return HandleCuttingKey(key, modifiers);

            if (UsesUvMouse && renderer.MeshEdit is { } uvTarget && uvEditor.Press(key, modifiers, uvTarget.Mesh, meshSelection))
                return true;

            if (regionStart is not null && key == Key.Escape)
            {
                EndRegion();
                return true;
            }

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

                case PreviewAction.DeleteMenu:
                    OpenMenu(MeshMenu.Delete);
                    return true;

                case PreviewAction.MergeMenu:
                    OpenMenu(MeshMenu.Merge);
                    return true;

                case PreviewAction.ExtrudeMenu:
                    OpenMenu(MeshMenu.Extrude);
                    return true;

                case PreviewAction.VertexMenu:
                    OpenMenu(MeshMenu.Vertex);
                    return true;

                case PreviewAction.EdgeMenu:
                    OpenMenu(MeshMenu.Edge);
                    return true;

                case PreviewAction.FaceMenu:
                    OpenMenu(MeshMenu.Face);
                    return true;

                case PreviewAction.NormalMenu:
                    OpenMenu(MeshMenu.Normal);
                    return true;

                case PreviewAction.UvMenu:
                    OpenMenu(MeshMenu.Uv);
                    return true;

                case PreviewAction.ToggleUvView:
                    ShowUv(!uvEditor.IsOn);
                    return true;

                case PreviewAction.AddMenu:
                    OpenMenu(MeshMenu.Add);
                    return true;

                case PreviewAction.Fill:
                    RunMesh(MeshOperation.Fill);
                    return true;

                case PreviewAction.Knife:
                    RunMesh(MeshOperation.Knife);
                    return true;

                case PreviewAction.Duplicate:
                    RunMesh(MeshOperation.Duplicate);
                    return true;

                case PreviewAction.Separate:
                    RunMesh(MeshOperation.Separate);
                    return true;

                case PreviewAction.Connect:
                    RunMesh(MeshOperation.Connect);
                    return true;

                case PreviewAction.Triangulate:
                    RunMesh(MeshOperation.Triangulate);
                    return true;

                case PreviewAction.TrianglesToQuads:
                    RunMesh(MeshOperation.TrianglesToQuads);
                    return true;

                case PreviewAction.NormalsOutside:
                    RunMesh(MeshOperation.NormalsOutside);
                    return true;

                case PreviewAction.NormalsInside:
                    RunMesh(MeshOperation.NormalsInside);
                    return true;

                case PreviewAction.Join:
                    Join();
                    return true;

                case PreviewAction.Crease:
                    RunMesh(MeshOperation.Crease);
                    return true;

                case PreviewAction.ToggleSidebar:
                    IsSidebarOpen = !IsSidebarOpen;
                    return true;

                case PreviewAction.ToggleSlide:
                    SlidesMesh = !SlidesMesh;
                    return true;

                case PreviewAction.SelectLinked:
                    Reselect(mesh => mesh.LinkedTo(meshSelection.Vertices));
                    return true;

                case PreviewAction.InvertSelection:
                    InvertMesh();
                    return true;

                case PreviewAction.GrowSelection:
                    Reselect(mesh => mesh.Grown(meshSelection.Vertices));
                    return true;

                case PreviewAction.ShrinkSelection:
                    Reselect(mesh => mesh.Shrunk(meshSelection.Vertices));
                    return true;

                case PreviewAction.TransformMove:
                    BeginTransform(TransformKind.Move);
                    return true;

                case PreviewAction.TransformRotate:
                    BeginTransform(TransformKind.Rotate);
                    return true;

                case PreviewAction.TransformScale:
                    BeginTransform(TransformKind.Scale);
                    return true;

                case PreviewAction.Inset:
                    Inset();
                    return true;

                case PreviewAction.Bevel:
                    Bevel();
                    return true;

                case PreviewAction.BoxSelect:
                    StartBoxOnce();
                    return true;

                case PreviewAction.CycleSelectShape:
                    CycleSelectShape();
                    return true;

                case PreviewAction.ToggleXRay:
                    IsXRay = !IsXRay;
                    return true;

                case PreviewAction.HideSelected:
                    HideMesh(false);
                    return true;

                case PreviewAction.HideOthers:
                    HideMesh(true);
                    return true;

                case PreviewAction.RevealHidden:
                    RevealMesh();
                    return true;

                case PreviewAction.ToggleProportional:
                    IsProportional = !IsProportional;
                    return true;

                case PreviewAction.ToggleConnected:
                    ConnectsProportional = !ConnectsProportional;
                    return true;

                case PreviewAction.CycleFalloff:
                    CycleFalloff();
                    return true;

                case PreviewAction.CyclePivot:
                    CyclePivot();
                    return true;

                case PreviewAction.CursorToSelected:
                    CursorToSelected();
                    return true;

                case PreviewAction.CursorToOrigin:
                    CursorToOrigin();
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
