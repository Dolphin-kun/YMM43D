using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics.Models;
using YMM43D.PreviewTool.Rendering;
using YMM43D.PreviewTool.Views;
using YMM43D.Project.Shape;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool.ViewModels
{
    public partial class Preview3DViewModel
    {
        private const float SpreadRatio = 0.5f;
        private const float WeldRatio = 0.25f;
        private const int SpinSteps = 12;
        private const float SmartAngle = 66f;

        private static readonly Color4 HintColor = new(1f, 0.718f, 0.302f, 1f);
        private static readonly Color4 BoneColor = new(1f, 0.835f, 0.31f, 1f);

        private readonly KnifeTool knife = new();
        private readonly EditSidebar sidebar = new();
        private readonly UvEditor uvEditor = new();

        private BisectKind? bisectKind;
        private AmountRedo? redo;
        private Vector2? bisectStart;

        public event Action<MeshMenu>? MenuRequested;

        public Bindable Sidebar => sidebar;

        public bool IsSidebarOpen { get; set => Set(ref field, value); }

        public double SidebarWidth { get; set => Set(ref field, Math.Clamp(value, MinSidebar, MaxSidebar)); } = 280;

        private const double MinSidebar = 200;
        private const double MaxSidebar = 640;

        public bool IsUvView
        {
            get => uvEditor.IsOn;
            set => ShowUv(value);
        }

        private string HiddenText()
        {
            var parts = new List<string>();

            if (meshSelection.HiddenVertices.Count > 0)
                parts.Add($"頂点 {meshSelection.HiddenVertices.Count}");

            if (meshSelection.HiddenEdges.Count > 0)
                parts.Add($"辺 {meshSelection.HiddenEdges.Count}");

            if (meshSelection.HiddenFaces.Count > 0)
                parts.Add($"面 {meshSelection.HiddenFaces.Count}");

            return string.Join("/", parts);
        }

        private ActionCommand meshCommand = null!;
        private long menuRevision = -1;

        private bool HoldsMouse => isEditingMesh && (modal.IsActive || amount.IsActive || uvEditor.IsMoving);

        private bool UsesUvMouse => isEditingMesh && uvEditor.IsOn && !modal.IsActive && !amount.IsActive && !IsCutting;

        private void ShowUv(bool on)
        {
            if (on && !isEditingMesh)
                on = false;

            if (on == uvEditor.IsOn)
                return;

            FinishModal(false);
            uvEditor.Toggle(on);
            OnPropertyChanged(nameof(IsUvView));
            TouchHint();
            RequestRender();
        }

        private UvSheet? UvSheet(int width, int height)
        {
            if (!isEditingMesh || !uvEditor.IsOn || renderer.MeshEdit is not { } edit)
                return null;

            var materials = edit.Parameter.ShownMaterials;
            var image = materials.Count > 0 ? ModelLibrary.FindImage(materials[Math.Clamp(sidebar.MaterialIndex, 0, materials.Count - 1)].Image) : null;

            return uvEditor.Sheet(edit.Mesh, meshSelection, new Vector2(width, height), image);
        }

        private void StoreUvs(Vector2[][] uvs)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked)
                return;

            var mesh = edit.Parameter.EnsureMesh();

            meshSelection.Bind(mesh);
            SeparateHistory();
            meshSelection.Bind(edit.Parameter.StoreMesh(mesh.WithUvs(uvs)));
            Refresh();
            SeparateHistory();
        }

        private bool IsCutting => knife.IsActive || bisectKind is not null;

        private string? CuttingHint
        {
            get
            {
                if (knife.IsActive)
                    return knife.Status;

                return bisectKind switch
                {
                    BisectKind.Cut => "二等分　ドラッグで切る線を引く　Esc でやめる",
                    BisectKind.Clear => "二等分　ドラッグで切る線を引く（線の左側が消え、右側が残ります）　Esc でやめる",
                    BisectKind.Fill => "二等分　ドラッグで切る線を引く（線の左側が消え、切り口に面を張ります）　Esc でやめる",
                    _ => null,
                };
            }
        }

        private List<PreviewLabel> Labels()
        {
            var labels = new List<PreviewLabel>();

            if (EditHint is { Length: > 0 } hint)
                labels.Add(new PreviewLabel(hint, HintColor));

            if (SelectedBoneName is { Length: > 0 } bone)
                labels.Add(new PreviewLabel(bone, BoneColor));

            if (FadingStatus() is { } status)
                labels.Add(status);

            return labels;
        }

        private static readonly Color4 StatusColor = new(0.85f, 0.85f, 0.85f, 1f);

        private const long StatusHoldMs = 1200;
        private const long StatusFadeMs = 600;

        private string shownStatus = string.Empty;
        private long? statusClearedAt;

        private PreviewLabel? FadingStatus()
        {
            var status = StatusLine;
            var now = Environment.TickCount64;

            if (status.Length > 0)
            {
                shownStatus = status;
                statusClearedAt = null;

                return new PreviewLabel(status, StatusColor, true);
            }

            if (shownStatus.Length == 0)
                return null;

            statusClearedAt ??= now;

            var waited = now - statusClearedAt.Value;
            var alpha = waited < StatusHoldMs ? 1f : 1f - (float)(waited - StatusHoldMs) / StatusFadeMs;

            if (alpha <= 0f)
            {
                shownStatus = string.Empty;
                statusClearedAt = null;

                return null;
            }

            RequestRender();

            return new PreviewLabel(shownStatus, new Color4(StatusColor.R, StatusColor.G, StatusColor.B, alpha), true);
        }

        private string StatusLine
        {
            get
            {
                var parts = new List<string>();

                if (isEditingMesh && pivot is not PivotPoint.Median)
                {
                    parts.Add(pivot switch
                    {
                        PivotPoint.Bounds => "回す中心：選んだ範囲の中心",
                        PivotPoint.Cursor => "回す中心：3Dカーソル",
                        PivotPoint.Individual => "回す中心：それぞれのまとまりの中心",
                        _ => "回す中心：最後に選んだところ",
                    });
                }

                if (isEditingMesh && IsProportional)
                    parts.Add($"プロポーショナル：{FalloffName}/{WorldScale.ToPixels(proportionalRadius):0}px{(ConnectsProportional ? "/つながりだけ" : string.Empty)}");

                if (SnapsToGrid)
                    parts.Add($"スナップ：{(isEditingMesh ? string.Join("/", SnapNames()) : $"{snapStep:0}px")}");

                return string.Join("　　", parts);
            }
        }

        private IEnumerable<string> SnapNames()
        {
            if ((snapTargets & SnapTargets.Increment) != 0)
                yield return $"{snapStep:0}px";

            if ((snapTargets & SnapTargets.Vertex) != 0)
                yield return "頂点";

            if ((snapTargets & SnapTargets.Edge) != 0)
                yield return "辺";

            if ((snapTargets & SnapTargets.Face) != 0)
                yield return "面";
        }

        private string FalloffName => falloff switch
        {
            ProportionalFalloff.Sphere => "球",
            ProportionalFalloff.Root => "ルート",
            ProportionalFalloff.InverseSquare => "逆二乗",
            ProportionalFalloff.Sharp => "鋭い",
            ProportionalFalloff.Linear => "直線",
            ProportionalFalloff.Constant => "一定",
            ProportionalFalloff.Random => "ランダム",
            _ => "なめらか",
        };

        private bool CanRunMesh(object? parameter)
        {
            if (!isEditingMesh || parameter is not string name || !Enum.TryParse<MeshOperation>(name, out var operation))
                return true;

            var (vertices, edges, faces) = (meshSelection.Vertices.Count, meshSelection.Edges.Count, meshSelection.Faces.Count);

            return operation switch
            {
                MeshOperation.DeleteVertices or MeshOperation.DissolveVertices or MeshOperation.MergeCenter or MeshOperation.MergeCursor or MeshOperation.MergeCollapse
                    or MeshOperation.MergeDistance or MeshOperation.ExtrudeVertices or MeshOperation.ExtrudeRegion or MeshOperation.Duplicate or MeshOperation.Separate or MeshOperation.Spin => vertices > 0,
                MeshOperation.MergeActive => meshSelection.Part is MeshPart.Vertices && meshSelection.Active >= 0,
                MeshOperation.Connect or MeshOperation.Fill => vertices > 1,
                MeshOperation.DeleteEdges or MeshOperation.DeleteOnlyEdgesAndFaces or MeshOperation.DissolveEdges or MeshOperation.ExtrudeEdges or MeshOperation.Subdivide
                    or MeshOperation.CutLoop or MeshOperation.RotateEdge or MeshOperation.RotateEdgeBack or MeshOperation.MarkSharp or MeshOperation.ClearSharp
                    or MeshOperation.Crease or MeshOperation.MarkSeam or MeshOperation.ClearSeam or MeshOperation.GridFill => edges > 0,
                MeshOperation.Bridge => edges > 1 || faces > 1,
                MeshOperation.DeleteFaces or MeshOperation.DeleteOnlyFaces or MeshOperation.DissolveFaces or MeshOperation.ExtrudeIndividual or MeshOperation.ExtrudeNormals
                    or MeshOperation.Triangulate or MeshOperation.TrianglesToQuads or MeshOperation.FlipNormals => faces > 0,
                _ => true,
            };
        }

        private void MoveUvsTo(Vector2 target)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked)
                return;

            var mesh = edit.Parameter.EnsureMesh();

            meshSelection.Bind(mesh);

            if (UvEditor.MovedTo(mesh, meshSelection, target) is not { } uvs)
                return;

            meshSelection.Bind(edit.Parameter.StoreMesh(mesh.WithUvs(uvs)));
            Refresh();
        }

        private void KeepRedo()
        {
            if (amount.TakeFinished() is { } finished)
                SetRedo(finished);
        }

        private void SetRedo(AmountRedo? next)
        {
            if (ReferenceEquals(redo, next))
                return;

            redo?.Applied -= OnRedoApplied;
            redo = next;
            redo?.Applied += OnRedoApplied;

            sidebar.Redo = next;
        }

        private void OnRedoApplied() => Refresh();

        public void SeparateSidebarEdit() => SeparateHistory();

        private bool ClicksSidebarTab(D3D11Host.MouseEventKind kind)
        {
            if (kind is not D3D11Host.MouseEventKind.Down || !SidebarTab.Contains(lastMouse, renderer.View.Width))
                return false;

            IsSidebarOpen = !IsSidebarOpen;

            return true;
        }

        private void WireSidebar()
        {
            sidebar.CenterMoved += pixels => MoveSelectionTo(WorldScale.ToWorldPosition(pixels.X, pixels.Y, pixels.Z));
            sidebar.CursorMoved += pixels => MoveCursor(WorldScale.ToWorldPosition(pixels.X, pixels.Y, pixels.Z));
            sidebar.RadiusChanged += pixels =>
            {
                proportionalRadius = WorldScale.ToWorld(pixels);
                TouchHint();
            };
            sidebar.MaterialAssigned += slot =>
            {
                var faces = meshSelection.Faces;

                Change(mesh => mesh.Assigned(faces, slot));
            };
            sidebar.MaterialChosen += ChooseMaterial;
            sidebar.KeyChosen += ChooseShapeKey;
            sidebar.UvMoved += MoveUvsTo;
            uvEditor.Committed += StoreUvs;
        }

        private void ChooseShapeKey(int key)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Parameter.ActiveShapeKey == key)
                return;

            FinishModal(false);
            edit.Parameter.ActiveShapeKey = key;
            Refresh();
        }

        private void ChooseMaterial(int slot, bool add)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit)
                return;

            var mesh = edit.Mesh;
            var faces = mesh.FacesOf(slot);
            IEnumerable<int> parts = meshSelection.Part switch
            {
                MeshPart.Faces => faces,
                MeshPart.Edges => faces.SelectMany(face => mesh.FaceEdges[face]).Distinct(),
                _ => faces.SelectMany(face => mesh.Faces[face]).Distinct(),
            };

            meshSelection.Apply(parts, add ? SelectOp.Add : SelectOp.Subtract);
            RequestRender();
        }

        private void ShowSidebar()
        {
            Vector3? center = isEditingMesh && renderer.MeshEdit is { } edit && !meshSelection.IsEmpty
                ? WorldScale.ToPixelOffset(MeshPivot.Median(edit.Mesh, edit.ToWorld, meshSelection.Vertices))
                : null;
            float? reach = isEditingMesh && IsProportional ? WorldScale.ToPixels(proportionalRadius) : null;

            var shape = isEditingMesh ? renderer.MeshEdit?.Parameter : null;
            IReadOnlyList<string> materials = shape is null ? [] : [.. shape.ShownMaterials.Select(material => material.Name)];
            IReadOnlyList<string> keys = shape is { ShapeKeys.Count: > 0 } ? ["基準", .. shape.ShapeKeys.Select(key => key.Name)] : [];

            var uvAt = isEditingMesh && uvEditor.IsOn && renderer.MeshEdit is { } uvTarget ? UvEditor.Center(uvTarget.Mesh, meshSelection) : null;

            sidebar.Show(isEditingMesh, center, WorldScale.ToPixelOffset(cursor), reach, materials, keys, shape?.ActiveShapeKey ?? -1, isEditingMesh && uvEditor.IsOn, uvAt);
            sidebar.ShowHidden(isEditingMesh ? HiddenText() : string.Empty);
        }

        private void OpenMenu(MeshMenu menu)
        {
            if (isEditingMesh)
                MenuRequested?.Invoke(menu);
        }

        private void RunMesh(object? parameter)
        {
            if (parameter is string name && Enum.TryParse<MeshOperation>(name, out var operation))
                RunMesh(operation);
        }

        private void RunMesh(MeshOperation operation)
        {
            if (!isEditingMesh || modal.IsActive || amount.IsActive || IsCutting)
                return;

            var vertices = meshSelection.Vertices;
            var edges = meshSelection.Edges;
            var faces = meshSelection.Faces;

            switch (operation)
            {
                case MeshOperation.DeleteVertices:
                    Change(mesh => mesh.DeleteVertices(vertices) is { } made ? (made, []) : null);
                    break;

                case MeshOperation.DeleteEdges:
                    Change(mesh => mesh.DeleteEdges(edges) is { } made ? (made, []) : null);
                    break;

                case MeshOperation.DeleteFaces:
                    Change(mesh => mesh.DeleteFaces(faces) is { } made ? (made, []) : null);
                    break;

                case MeshOperation.DeleteOnlyFaces:
                    Change(mesh => mesh.DeleteOnlyFaces(faces) is { } made ? (made, []) : null);
                    break;

                case MeshOperation.DeleteOnlyEdgesAndFaces:
                    Change(mesh => mesh.DeleteOnlyEdgesAndFaces(edges) is { } made ? (made, []) : null);
                    break;

                case MeshOperation.DissolveVertices:
                    Change(mesh => mesh.DissolveVertices(vertices));
                    break;

                case MeshOperation.DissolveEdges:
                    Change(mesh => mesh.DissolveEdges(edges));
                    break;

                case MeshOperation.DissolveFaces:
                    Change(mesh => mesh.DissolveFaces(faces));
                    break;

                case MeshOperation.MergeCenter:
                    Change(mesh => mesh.MergeAt(vertices, mesh.Middle(vertices)));
                    break;

                case MeshOperation.MergeCursor:
                    if (LocalCursor() is { } spot)
                        Change(mesh => mesh.MergeAt(vertices, spot));

                    break;

                case MeshOperation.MergeCollapse:
                    Change(mesh => mesh.Collapse(vertices));
                    break;

                case MeshOperation.MergeActive:
                    var active = meshSelection.Part is MeshPart.Vertices ? meshSelection.Active : -1;

                    Change(mesh => active >= 0 && active < mesh.Points.Length ? mesh.MergeAt(vertices, mesh.Points[active]) : null);
                    break;

                case MeshOperation.MergeDistance:
                    BeginAmount(edit => new AmountSpec("距離でくっつける", AmountUnit.Length, Step(edit.Mesh) * WeldRatio), (mesh, chosen, value) => mesh.MergeByDistance(chosen.Vertices, value.Amount) is { } made ? (made.Mesh, made.Added) : null);
                    break;

                case MeshOperation.ExtrudeRegion:
                    Extrude();
                    break;

                case MeshOperation.ExtrudeEdges:
                    ExtrudeThen(mesh => mesh.Extrude([], edges, []), false);
                    break;

                case MeshOperation.ExtrudeVertices:
                    ExtrudeThen(mesh => mesh.Extrude([], [], vertices), false);
                    break;

                case MeshOperation.ExtrudeNormals:
                    BeginAmount(edit => new AmountSpec("法線に沿って押し出す", AmountUnit.Length, Step(edit.Mesh) * SpreadRatio, true), (mesh, chosen, value) => mesh.ExtrudeAlongNormals(chosen.Faces, value.Amount));
                    break;

                case MeshOperation.ExtrudeIndividual:
                    BeginAmount(edit => new AmountSpec("面をばらばらに押し出す", AmountUnit.Length, Step(edit.Mesh) * SpreadRatio, true), (mesh, chosen, value) => mesh.ExtrudeIndividual(chosen.Faces, value.Amount));
                    break;

                case MeshOperation.Spin:
                    Spin();
                    break;

                case MeshOperation.Subdivide:
                    BeginAmount(_ => new AmountSpec("細かく分ける", AmountUnit.Count, 0f, CountLabel: "分ける数", Count: 1, LeastCount: 1), (mesh, chosen, value) => mesh.Subdivide(chosen.Edges, value.Count));
                    break;

                case MeshOperation.CutLoop:
                    CutLoop();
                    break;

                case MeshOperation.Bridge:
                    BeginAmount(_ => new AmountSpec("橋渡し", AmountUnit.Count, 0f, CountLabel: "途中の段", Count: 0, LeastCount: 0), (mesh, chosen, value) => mesh.Bridge(chosen.Faces, chosen.Edges, value.Count));
                    break;

                case MeshOperation.RotateEdge:
                    Change(mesh => mesh.RotateEdges(edges, false));
                    break;

                case MeshOperation.RotateEdgeBack:
                    Change(mesh => mesh.RotateEdges(edges, true));
                    break;

                case MeshOperation.Connect:
                    Change(mesh => mesh.Connect(vertices));
                    break;

                case MeshOperation.Knife:
                    StartKnife();
                    break;

                case MeshOperation.Bisect:
                    StartBisect(BisectKind.Cut);
                    break;

                case MeshOperation.BisectClear:
                    StartBisect(BisectKind.Clear);
                    break;

                case MeshOperation.BisectFill:
                    StartBisect(BisectKind.Fill);
                    break;

                case MeshOperation.Fill:
                    Change(mesh => mesh.Fill(vertices, edges));
                    break;

                case MeshOperation.GridFill:
                    BeginAmount(edit => new AmountSpec("グリッドで埋める", AmountUnit.Count, 0f, CountLabel: "幅", Count: edit.Mesh.DefaultSpan(edges), LeastCount: 1, ShiftLabel: "ずらし"), (mesh, chosen, value) => mesh.GridFill(chosen.Edges, value.Count, value.Shift));
                    break;

                case MeshOperation.Triangulate:
                    Change(mesh => mesh.Triangulate(faces));
                    break;

                case MeshOperation.TrianglesToQuads:
                    Change(mesh => mesh.TrianglesToQuads(faces));
                    break;

                case MeshOperation.FlipNormals:
                    Change(mesh => mesh.FlipNormals(faces));
                    break;

                case MeshOperation.NormalsOutside:
                    Change(mesh => mesh.RecalculateNormals(faces, false));
                    break;

                case MeshOperation.NormalsInside:
                    Change(mesh => mesh.RecalculateNormals(faces, true));
                    break;

                case MeshOperation.Duplicate:
                    ExtrudeThen(mesh => mesh.Duplicate(faces, edges, vertices), false);
                    break;

                case MeshOperation.Separate:
                    Separate();
                    break;

                case MeshOperation.ShadeSmooth:
                    Change(mesh => mesh.Shaded(faces.Count > 0 ? faces : [.. Enumerable.Range(0, mesh.Faces.Length)], true));
                    break;

                case MeshOperation.ShadeFlat:
                    Change(mesh => mesh.Shaded(faces.Count > 0 ? faces : [.. Enumerable.Range(0, mesh.Faces.Length)], false));
                    break;

                case MeshOperation.ShadeAutoSmooth:
                    Change(mesh => mesh.Shaded(faces.Count > 0 ? faces : [.. Enumerable.Range(0, mesh.Faces.Length)], true), parameter => parameter.AutoSmooth = true);
                    break;

                case MeshOperation.MarkSharp:
                    Change(mesh => mesh.Marked(edges, mark => mark with { Sharp = true }));
                    break;

                case MeshOperation.ClearSharp:
                    Change(mesh => mesh.Marked(edges, mark => mark with { Sharp = false }));
                    break;

                case MeshOperation.Crease:
                    BeginAmount(edit => new AmountSpec("クリース", AmountUnit.Factor, edit.Mesh.AverageCrease(edges)), (mesh, chosen, value) => mesh.Marked(chosen.Edges, mark => mark with { Crease = value.Amount }));
                    break;

                case MeshOperation.MarkSeam:
                    Change(mesh => mesh.Marked(edges, mark => mark with { Seam = true }));
                    break;

                case MeshOperation.ClearSeam:
                    Change(mesh => mesh.Marked(edges, mark => mark with { Seam = false }));
                    break;

                case MeshOperation.Unwrap:
                    Change(mesh => mesh.Unwrapped(FacesOrAll(mesh, faces)));
                    break;

                case MeshOperation.SmartProject:
                    BeginAmount(_ => new AmountSpec("スマート UV 投影", AmountUnit.Angle, float.DegreesToRadians(SmartAngle)), (mesh, chosen, value) => mesh.SmartProjected(FacesOrAll(mesh, chosen.Faces), value.Amount));
                    break;

                case MeshOperation.CubeProject:
                    Change(mesh => mesh.CubeProjected(FacesOrAll(mesh, faces)));
                    break;

                case MeshOperation.CylinderProject:
                    Change(mesh => mesh.CylinderProjected(FacesOrAll(mesh, faces)));
                    break;

                case MeshOperation.SphereProject:
                    Change(mesh => mesh.SphereProjected(FacesOrAll(mesh, faces)));
                    break;

                case MeshOperation.ViewProject:
                    if (renderer.MeshEdit is { } shown)
                    {
                        var view = renderer.View;
                        var toScreen = shown.ToWorld * view.ViewProjection * Matrix4x4.CreateScale(view.Width, view.Height, 1f);

                        Change(mesh => mesh.ViewProjected(FacesOrAll(mesh, faces), toScreen));
                    }

                    break;

                case MeshOperation.ResetUvs:
                    Change(mesh => mesh.ResetUvs(FacesOrAll(mesh, faces)));
                    break;

                case MeshOperation.PackIslands:
                    Change(mesh => mesh.Packed(FacesOrAll(mesh, faces)));
                    break;

                case MeshOperation.AddPlane:
                    AddShape(MeshShape.Plane, "平面を足す");
                    break;

                case MeshOperation.AddCube:
                    AddShape(MeshShape.Cube, "立方体を足す");
                    break;

                case MeshOperation.AddCircle:
                    AddShape(MeshShape.Circle, "円を足す");
                    break;

                case MeshOperation.AddUvSphere:
                    AddShape(MeshShape.UvSphere, "UV 球を足す");
                    break;

                case MeshOperation.AddIcoSphere:
                    AddShape(MeshShape.IcoSphere, "ICO 球を足す");
                    break;

                case MeshOperation.AddCylinder:
                    AddShape(MeshShape.Cylinder, "円柱を足す");
                    break;

                case MeshOperation.AddCone:
                    AddShape(MeshShape.Cone, "円錐を足す");
                    break;

                case MeshOperation.AddTorus:
                    AddShape(MeshShape.Torus, "ドーナツを足す");
                    break;

                case MeshOperation.AddGrid:
                    AddShape(MeshShape.Grid, "グリッドを足す");
                    break;

                case MeshOperation.Import:
                    ImportShape();
                    break;
            }
        }

        public ICommand ExportObjCommand => field ??= new ActionCommand(_ => true, _ => ExportShape(false));

        public ICommand ExportGlbCommand => field ??= new ActionCommand(_ => true, _ => ExportShape(true));

        private void AddShape(MeshShape shape, string label)
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked || LocalCursor() is not { } at)
                return;

            var (countLabel, least, start) = MeshAdd.CountOf(shape);

            AddMesh(edit, new AmountSpec(label, AmountUnit.Length, 1f, CountLabel: countLabel, Count: start, LeastCount: least), (mesh, chosen) =>
                chosen.Amount <= 1e-6f ? null : mesh.Added(MeshAdd.Make(shape, chosen.Count), Matrix4x4.CreateScale(chosen.Amount) * Matrix4x4.CreateTranslation(at)));
        }

        private void ImportShape()
        {
            if (!isEditingMesh || renderer.MeshEdit is not { } edit || edit.Item.IsLocked || LocalCursor() is not { } at)
                return;

            var dialog = new OpenFileDialog { Filter = ShapeImport.Filter, Title = "形を読み込む" };

            if (dialog.ShowDialog() != true)
                return;

            ShapeImport.Imported imported;

            try
            {
                imported = ShapeImport.Load(dialog.FileName);
            }
            catch (Exception error)
            {
                MessageBox.Show($"{Path.GetFileName(dialog.FileName)} を読み込めませんでした。\n{error.Message}", "YMM43D", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (imported.Mesh.Points.Length == 0)
                return;

            var parameter = edit.Parameter;
            var takesMaterials = imported.Materials.Count > 0;
            var resets = takesMaterials && (!parameter.UsesMaterials || parameter.Materials.Count == 0);
            var first = resets ? 1 : parameter.Materials.Count;
            var (min, max) = MeshUv.Bounds(imported.Mesh.Points);
            var longest = MathF.Max(MathF.Max(max.X - min.X, max.Y - min.Y), MathF.Max(max.Z - min.Z, 1e-6f));
            var middle = (min + max) / 2f;
            var shape = takesMaterials ? imported.Mesh.With(imported.Mesh.Points, imported.Mesh.Faces, imported.Mesh.Groups.Select(group => group + first), imported.Mesh.Smooth).WithUvs(imported.Mesh.Uvs) : imported.Mesh;

            AddMesh(edit, new AmountSpec($"{Path.GetFileName(dialog.FileName)} を読み込む", AmountUnit.Length, 1f), (mesh, chosen) =>
            {
                if (chosen.Amount <= 1e-6f)
                    return null;

                var cleared = resets && mesh.Faces.Length > 0 ? mesh.Assigned([.. Enumerable.Range(0, mesh.Faces.Length)], 0)?.Mesh ?? mesh : mesh;
                var place = Matrix4x4.CreateTranslation(-middle) * Matrix4x4.CreateScale(chosen.Amount / longest) * Matrix4x4.CreateTranslation(at);

                return cleared.Added(shape, place, takesMaterials);
            }, () => parameter.TakeMaterials(imported.Materials));
        }

        private void AddMesh(in MeshEditTarget edit, AmountSpec spec, Func<MeshTopology, AmountValue, (MeshTopology Mesh, int[] Added)?> make, Action? also = null)
        {
            var value = new AmountValue(spec.Start, spec.Count, 0);
            Func<MeshTopology, ChosenParts, AmountValue, (MeshTopology Mesh, int[] Added)?> work = (mesh, _, chosen) => make(mesh, chosen);
            var parts = new ChosenParts([], [], []);
            var mesh = edit.Parameter.EnsureMesh();

            meshSelection.Bind(mesh);

            if (work(mesh, parts, value) is not { } made)
                return;

            var scale = MathF.Max((Vector3.TransformNormal(Vector3.UnitX, edit.ToWorld).Length() + Vector3.TransformNormal(Vector3.UnitY, edit.ToWorld).Length() + Vector3.TransformNormal(Vector3.UnitZ, edit.ToWorld).Length()) / 3f, 1e-6f);

            SeparateHistory();
            also?.Invoke();

            var current = edit.Parameter.StoreMesh(made.Mesh.Carry(mesh));

            meshSelection.Replace(current, made.Added);
            Refresh();
            SeparateHistory();
            SetRedo(new AmountRedo(edit.Parameter, mesh, parts, spec, scale, work, meshSelection, value, current));
        }

        private void ExportShape(bool binary)
        {
            var target = isEditingMesh ? renderer.MeshEdit : selection.Count == 1 ? renderer.ResolveMeshShape(selection[0]) : null;

            if (target is not { } shape)
            {
                MessageBox.Show("書き出す 3Dアイテムを 1 つ選んでください。", "YMM43D", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = binary ? "glTF バイナリ (*.glb)|*.glb" : "Wavefront OBJ (*.obj)|*.obj",
                FileName = binary ? "shape.glb" : "shape.obj",
                Title = "形を書き出す",
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var geometry = shape.Parameter.ExportGeometry(shape.Time);
                var slots = shape.Parameter.ExportSlots(shape.Time);

                if (binary)
                    ShapeExport.WriteGlb(dialog.FileName, geometry, slots);
                else
                    ShapeExport.WriteObj(dialog.FileName, geometry, slots);
            }
            catch (Exception error)
            {
                MessageBox.Show($"書き出せませんでした。\n{error.Message}", "YMM43D", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static IReadOnlyCollection<int> FacesOrAll(MeshTopology mesh, IReadOnlyCollection<int> faces)
            => faces.Count > 0 ? faces : [.. Enumerable.Range(0, mesh.Faces.Length)];

        private void Extrude()
        {
            var faces = meshSelection.Faces;

            ExtrudeThen(mesh => mesh.Extrude(faces, meshSelection.Edges, meshSelection.Vertices), faces.Count > 0);
        }

        private void ExtrudeThen(Func<MeshTopology, (MeshTopology Mesh, int[] Added)?> work, bool alongNormal)
        {
            var before = meshRevision;
            var along = alongNormal && renderer.ResolveMeshShape() is { } start ? Normal(start) : (Vector3?)null;

            Change(work);

            if (meshRevision != before)
                BeginTransform(TransformKind.Move, along);
        }

        private Vector3? LocalCursor()
            => renderer.MeshEdit is { } edit && Matrix4x4.Invert(edit.ToWorld, out var back) ? Vector3.Transform(cursor, back) : null;

        private void Spin()
        {
            if (renderer.ResolveMeshShape() is not { } edit || !Matrix4x4.Invert(edit.ToWorld, out var back))
                return;

            var axis = Vector3.Normalize(-renderer.View.Forward);
            var center = cursor;
            var toWorld = edit.ToWorld;

            BeginAmount(
                _ => new AmountSpec("回して押し出す", AmountUnit.Angle, MathF.Tau, CountLabel: "段数", Count: SpinSteps, LeastCount: 1, Around: center),
                (mesh, chosen, value) =>
                {
                    var turn = Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateFromAxisAngle(axis, value.Amount / value.Count) * Matrix4x4.CreateTranslation(center);
                    var closes = MathF.Abs(MathF.Abs(value.Amount) - MathF.Tau) < 1e-3f;

                    return MathF.Abs(value.Amount) < 1e-4f ? null : mesh.Spin(chosen.Faces, chosen.Edges, chosen.Vertices, toWorld * turn * back, value.Count, closes);
                });
        }

        private void BeginAmount(Func<MeshEditTarget, AmountSpec> spec, Func<MeshTopology, ChosenParts, AmountValue, (MeshTopology Mesh, int[] Added)?> work)
        {
            if (!isEditingMesh || renderer.ResolveMeshShape() is not { } edit)
                return;

            SeparateHistory();

            if (amount.Begin(spec(edit), work, edit, meshSelection, lastMouse, renderer.View))
                UpdateModal();
        }

        private void Separate()
        {
            if (timeline is null || renderer.MeshEdit is not { } edit || edit.Item.IsLocked)
                return;

            var mesh = edit.Parameter.EnsureMesh();

            meshSelection.Bind(mesh);

            if (mesh.Separate(meshSelection.Faces, meshSelection.Edges, meshSelection.Vertices) is not { } split
                || edit.Item.GetClone() is not ShapeItem { ShapeParameter: Shape3DParameter copied } clone)
            {
                return;
            }

            SeparateHistory();

            copied.StoreMesh(split.Part);
            meshSelection.Replace(edit.Parameter.StoreMesh(split.Left), []);
            timeline.TryAddItems([clone], clone.Frame, clone.Layer + 1, false);

            Refresh();
            SeparateHistory();
        }

        private void Join()
        {
            if (isEditingMesh || timeline is null)
                return;

            var targets = selection
                .Where(item => !item.IsLocked)
                .Select(renderer.ResolveMeshShape)
                .OfType<MeshEditTarget>()
                .ToList();

            if (targets.Count < 2 || !Matrix4x4.Invert(targets[0].ToWorld, out var back))
                return;

            var into = targets[0];
            var mesh = into.Parameter.EnsureMesh();

            foreach (var other in targets.Skip(1))
                mesh = mesh.Join(other.Mesh, other.ToWorld * back);

            SeparateHistory();

            into.Parameter.StoreMesh(mesh);
            timeline.DeleteItems([.. targets.Skip(1).Select(target => target.Item)]);
            Select([into.Item]);

            Refresh();
            SeparateHistory();
        }

        private void StartKnife()
        {
            if (renderer.MeshEdit is null)
                return;

            bisectKind = null;
            knife.Begin(lastMouse);
            TouchHint();
            RequestRender();
        }

        private void FinishKnife(bool apply)
        {
            var cuts = knife.Cuts();

            if (apply && cuts.Count > 0)
                Change(mesh => mesh.Knife(cuts));

            knife.End();
            TouchHint();
            RequestRender();
        }

        private void StartBisect(BisectKind kind)
        {
            if (renderer.MeshEdit is null)
                return;

            knife.End();
            bisectKind = kind;
            bisectStart = null;
            TouchHint();
            RequestRender();
        }

        private void FinishBisect(bool apply)
        {
            if (apply && bisectKind is { } kind && bisectStart is { } from && Plane(from, lastMouse) is { } plane)
            {
                var faces = meshSelection.Faces;

                Change(mesh => mesh.Bisect(faces.Count > 0 ? faces : [.. Enumerable.Range(0, mesh.Faces.Length)], plane.Point, plane.Normal, kind is not BisectKind.Cut, false, kind is BisectKind.Fill));
            }

            bisectKind = null;
            bisectStart = null;
            TouchHint();
            RequestRender();
        }

        private (Vector3 Point, Vector3 Normal)? Plane(Vector2 from, Vector2 to)
        {
            var view = renderer.View;

            if (Vector2.Distance(from, to) < ClickSlop
                || renderer.MeshEdit is not { } edit
                || !Matrix4x4.Invert(edit.ToWorld, out var back)
                || view.Ray(from) is not { } first
                || view.Ray(to) is not { } second)
            {
                return null;
            }

            var normal = Vector3.Cross(first.Direction, second.Origin + second.Direction - first.Origin);
            var span = to - from;
            var right = (from + to) / 2f + Vector2.Normalize(new Vector2(-span.Y, span.X)) * 20f;

            if (normal.LengthSquared() < 1e-12f || view.Ray(right) is not { } side)
                return null;

            if (Vector3.Dot(side.Origin + side.Direction - first.Origin, normal) < 0f)
                normal = -normal;

            return (Vector3.Transform(first.Origin, back), Vector3.TransformNormal(normal, Matrix4x4.Transpose(edit.ToWorld)));
        }

        private bool HandleCuttingMouse(D3D11Host.MouseEventKind kind)
        {
            if (knife.IsActive)
            {
                switch (kind)
                {
                    case D3D11Host.MouseEventKind.Move:
                        knife.Mouse = lastMouse;
                        RequestRender();
                        return false;

                    case D3D11Host.MouseEventKind.Down when renderer.MeshPick is { } pick:
                        knife.Add(pick, meshSelection, lastMouse);
                        RequestRender();
                        return true;

                    case D3D11Host.MouseEventKind.RightDown:
                        FinishKnife(false);
                        return true;

                    default:
                        return false;
                }
            }

            switch (kind)
            {
                case D3D11Host.MouseEventKind.Move:
                    RequestRender();
                    return bisectStart is not null;

                case D3D11Host.MouseEventKind.Down:
                    bisectStart = lastMouse;
                    return true;

                case D3D11Host.MouseEventKind.Up when bisectStart is not null:
                    FinishBisect(true);
                    return true;

                case D3D11Host.MouseEventKind.RightDown:
                    FinishBisect(false);
                    return true;

                default:
                    return false;
            }
        }

        private bool HandleCuttingKey(Key key, ModifierKeys modifiers)
        {
            if (knife.IsActive)
            {
                switch (key)
                {
                    case Key.Enter or Key.Space:
                        FinishKnife(true);
                        return true;

                    case Key.Escape:
                        FinishKnife(false);
                        return true;

                    case Key.Z:
                        knife.ToggleThrough();
                        break;

                    case Key.E:
                        knife.NewLine();
                        break;

                    case Key.Back:
                        knife.Undo();
                        break;

                    default:
                        RunView(key, modifiers);
                        return true;
                }

                TouchHint();
                RequestRender();
                return true;
            }

            if (key == Key.Escape)
                FinishBisect(false);
            else
                RunView(key, modifiers);

            return true;
        }

        private void RunView(Key key, ModifierKeys modifiers)
        {
            if (PreviewShortcuts.Find(key, modifiers, true) is { } action && action <= PreviewAction.LevelRoll)
                Run(action);
        }

        private (IReadOnlyList<IReadOnlyList<Vector2>> Lines, IReadOnlyList<Vector2> Marks) CuttingOverlay()
        {
            if (knife.IsActive && renderer.MeshPick is { } pick)
                return ([.. knife.Lines(renderer.View)], [.. knife.Marks(pick, meshSelection)]);

            if (bisectStart is { } from)
                return ([[from, lastMouse]], []);

            return ([], []);
        }
    }
}
