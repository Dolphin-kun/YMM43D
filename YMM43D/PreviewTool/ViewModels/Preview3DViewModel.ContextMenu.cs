using System.Windows;
using System.Windows.Input;
using YMM43D.PreviewTool.Views;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool.ViewModels
{
    public partial class Preview3DViewModel
    {
        private Point? contextClickAt;

        public ICommand ToggleEditCommand => field ??= new ActionCommand(_ => isEditingMesh || CanEditMesh, _ => IsEditingMesh = !isEditingMesh);

        private void TrackContextClick(Point position, D3D11Host.MouseEventKind kind)
        {
            switch (kind)
            {
                case D3D11Host.MouseEventKind.RightDown:
                    contextClickAt = position;
                    break;

                case D3D11Host.MouseEventKind.Move when contextClickAt is { } at && (position - at).Length > ClickSlop:
                    contextClickAt = null;
                    break;

                case D3D11Host.MouseEventKind.RightUp when contextClickAt is not null:
                    contextClickAt = null;
                    OpenContextMenu(position);
                    break;
            }
        }

        private void OpenContextMenu(Point position)
        {
            if (!isEditingMesh)
                ChooseUnder(position);

            MenuRequested?.Invoke(isEditingMesh ? MeshContextEntries() : ItemContextEntries());
        }

        private void ChooseUnder(Point position)
        {
            renderer.RequestPick(ToVector(position));
            d3dHost?.RenderFrame();

            if (renderer.TakePickResult() is { } picked && !selection.Contains(picked.Item))
                Select([picked.Item]);
        }

        private IReadOnlyList<MeshMenuEntry> ItemContextEntries() =>
        [
            Run("編集モードに入る", ToggleEditCommand, PreviewAction.ToggleEditMode, "3Dアイテムを1つだけ選んでいるとき、その形そのものを編集します。"),
            Run("選んだものを注目", FocusSelectedCommand, PreviewAction.FocusSelected, "選んでいるアイテムが画面に収まるまで寄り、以後の回り込みもそのアイテムを中心に効きます。"),
            MeshMenuItems.Separator,
            Run("選んだアイテムをくっつける", JoinCommand, PreviewAction.Join, "選んでいる 3D アイテムの形を最初に選んだアイテムへまとめて 1 つにし、ほかのアイテムは消えます。"),
            MeshMenuItems.Separator,
            Run("書き出す", null, null, null,
            [
                Run("OBJ（.obj ＋ .mtl）…", ExportObjCommand, null, "選んでいる 3Dアイテムの形を、Blender などで読める OBJ と材質の MTL に書き出します。"),
                Run("glTF バイナリ（.glb）…", ExportGlbCommand, null, "選んでいる 3Dアイテムの形を、Blender などで読める 1 つのファイルに材質と画像ごと書き出します。"),
            ]),
        ];

        private IReadOnlyList<MeshMenuEntry> MeshContextEntries()
        {
            var menus = MeshMenus;
            var separator = MeshMenuItems.Separator;
            var separate = menus.Entry("別のアイテムに分ける", MeshOperation.Separate, PreviewAction.Separate, "選んだところを切り取り、同じ設定の新しい 3D アイテムにします。");

            if (EditsBones)
                return [Run("骨の編集をやめる", ToggleBoneEditCommand, null, "形の頂点・辺・面を選ぶ操作に戻します。")];

            IReadOnlyList<MeshMenuEntry> entries = meshSelection.Part switch
            {
                MeshPart.Edges =>
                [
                    menus.Entry(MeshOperation.Subdivide),
                    menus.Entry(MeshOperation.ExtrudeEdges),
                    Run("角を削る（ベベル）", BevelCommand, PreviewAction.Bevel, "選んでいる面を少し縮めて辺と角に面を足し、削る量はマウスの距離か数字（px）で決めます。"),
                    menus.Entry(MeshOperation.Bridge),
                    menus.Entry(MeshOperation.CutLoop),
                    separator,
                    menus.Entry("ナイフ", MeshOperation.Knife, PreviewAction.Knife, "クリックで点を打って引いた線に沿って面を切り、Enter で切って Esc でやめます。"),
                    menus.Entry("二等分", MeshOperation.Bisect, null, "ドラッグで引いた線を画面の奥へ伸ばした平面で、選んだ面（選んでいなければ全体）を切ります。"),
                    separator,
                    menus.Entry(MeshOperation.RotateEdge),
                    menus.Entry(MeshOperation.RotateEdgeBack),
                    separator,
                    menus.Entry(MeshOperation.MarkSeam),
                    menus.Entry(MeshOperation.ClearSeam),
                    menus.Entry(MeshOperation.MarkSharp),
                    menus.Entry(MeshOperation.ClearSharp),
                    menus.Entry(MeshOperation.Crease),
                    separator,
                    separate,
                    separator,
                    menus.Entry(MeshOperation.DissolveEdges),
                    menus.Entry(MeshOperation.DeleteEdges, "辺を消す"),
                ],
                MeshPart.Faces =>
                [
                    menus.Entry(MeshOperation.Subdivide),
                    menus.Entry(MeshOperation.ExtrudeRegion),
                    menus.Entry(MeshOperation.ExtrudeNormals),
                    menus.Entry(MeshOperation.ExtrudeIndividual),
                    Run("差し込む", InsetCommand, PreviewAction.Inset, "選んでいる面の内側に一回り小さい面を作り、幅はマウスの距離か数字（px）で決めます。"),
                    separator,
                    menus.Entry(MeshOperation.Triangulate),
                    menus.Entry(MeshOperation.TrianglesToQuads),
                    Run("厚みを付ける", SolidifyCommand, null, "ぺらい面の裏に面と側面を足して中身のある形にし、厚みはマウスの距離か数字（px、- で内側）で決めます。"),
                    separator,
                    menus.Entry(MeshOperation.ShadeSmooth),
                    menus.Entry(MeshOperation.ShadeFlat),
                    menus.Entry(MeshOperation.ShadeAutoSmooth),
                    menus.Entry(MeshOperation.FlipNormals),
                    separator,
                    separate,
                    separator,
                    menus.Entry(MeshOperation.DissolveFaces),
                    menus.Entry(MeshOperation.DeleteFaces, "面を消す"),
                ],
                _ =>
                [
                    menus.Entry(MeshOperation.Subdivide),
                    menus.Entry(MeshOperation.ExtrudeVertices),
                    menus.Entry(MeshOperation.Fill),
                    menus.Entry(MeshOperation.Connect),
                    menus.Entry(MeshOperation.SmoothVertices),
                    separator,
                    Run("くっつける", null, PreviewAction.MergeMenu, null, menus.Entries(MeshMenu.Merge)),
                    separate,
                    separator,
                    menus.Entry(MeshOperation.DissolveVertices),
                    menus.Entry(MeshOperation.DeleteVertices, "頂点を消す"),
                ],
            };

            return renderer.MeshEdit is { Parameter.HasBones: true }
                ? [Run("骨を編集", ToggleBoneEditCommand, null, "骨の付け根・先・線をドラッグして動かせるようにします。"), separator, .. entries]
                : entries;
        }

        private static MeshMenuEntry Run(string header, ICommand? command, PreviewAction? key, string? tip, IReadOnlyList<MeshMenuEntry>? items = null)
            => new(header, command, string.Empty, key is { } shortcut ? PreviewShortcuts.TextOf(shortcut) : string.Empty, tip, false, items);
    }
}
