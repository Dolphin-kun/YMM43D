using System.Windows.Input;

namespace YMM43D.PreviewTool
{
    public sealed class MeshMenuItems(ICommand command)
    {
        private static readonly Dictionary<MeshMenu, (string Header, MeshOperation? Operation, PreviewAction? Key, string? Tip)[]> Table = new()
        {
            [MeshMenu.Delete] =
            [
                ("頂点", MeshOperation.DeleteVertices, null, "選んだ頂点と、それを使う辺・面を消します。"),
                ("辺", MeshOperation.DeleteEdges, null, "選んだ辺と、それに接する面を消します。"),
                ("面", MeshOperation.DeleteFaces, null, "選んだ面と、ほかで使われていない辺・頂点を消します。"),
                ("面だけ", MeshOperation.DeleteOnlyFaces, null, "選んだ面だけを消し、辺と頂点は残します。"),
                ("辺と面だけ", MeshOperation.DeleteOnlyEdgesAndFaces, null, "選んだ辺とそれに接する面を消し、頂点は残します。"),
                ("", null, null, null),
                ("頂点を溶かす", MeshOperation.DissolveVertices, null, "まわりの面を 1 枚にまとめて、穴を空けずに頂点だけを取り除きます。"),
                ("辺を溶かす", MeshOperation.DissolveEdges, null, "辺の両側の面を 1 枚にまとめて、穴を空けずに辺だけを取り除きます。"),
                ("面を溶かす", MeshOperation.DissolveFaces, null, "となり合う選んだ面を 1 枚の面にまとめます。"),
            ],
            [MeshMenu.Merge] =
            [
                ("中心に", MeshOperation.MergeCenter, null, "選んだ頂点を、それらの中心の 1 点にまとめます。"),
                ("3Dカーソルに", MeshOperation.MergeCursor, null, "選んだ頂点を、3D カーソルの位置の 1 点にまとめます。"),
                ("つながりごとに", MeshOperation.MergeCollapse, null, "辺でつながっているまとまりごとに、その中心へまとめます。"),
                ("最後に選んだ頂点に", MeshOperation.MergeActive, null, "選んだ頂点を、最後にクリックした頂点の位置へまとめます。"),
                ("近いものどうし", MeshOperation.MergeDistance, null, "決めた距離より近い頂点どうしをまとめます。距離はマウスか数字（px）で決めます。"),
            ],
            [MeshMenu.Extrude] =
            [
                ("押し出す", MeshOperation.ExtrudeRegion, PreviewAction.Extrude, "選んだ面・辺・頂点を引き出して、そのまま動かします。"),
                ("法線に沿って押し出す", MeshOperation.ExtrudeNormals, null, "選んだ面を引き出し、頂点ごとに面の向きへ同じ距離だけ動かします。"),
                ("面をばらばらに押し出す", MeshOperation.ExtrudeIndividual, null, "選んだ面を 1 枚ずつ別々に、それぞれの向きへ引き出します。"),
                ("辺だけ押し出す", MeshOperation.ExtrudeEdges, null, "選んだ辺を引き出して、帯のような面を作ります。"),
                ("頂点だけ押し出す", MeshOperation.ExtrudeVertices, null, "選んだ頂点を引き出して、辺を伸ばします。"),
                ("", null, null, null),
                ("回して押し出す（スピン）", MeshOperation.Spin, null, "3D カーソルを中心に、画面の奥へ向かう軸で回しながら押し出します。角度はマウスか数字、段数はホイールで決めます。"),
            ],
            [MeshMenu.Vertex] =
            [
                ("頂点をつなぐ", MeshOperation.Connect, PreviewAction.Connect, "同じ面にある選んだ頂点どうしを辺で結び、面を分けます。"),
                ("面を張る", MeshOperation.Fill, PreviewAction.Fill, "選んだ頂点を囲む面を 1 枚作ります。2 点なら辺を引きます。"),
                ("頂点だけ押し出す", MeshOperation.ExtrudeVertices, null, "選んだ頂点を引き出して、辺を伸ばします。"),
                ("近いものどうしをくっつける", MeshOperation.MergeDistance, null, "決めた距離より近い頂点どうしをまとめます。"),
                ("頂点を溶かす", MeshOperation.DissolveVertices, null, "穴を空けずに頂点だけを取り除きます。"),
            ],
            [MeshMenu.Edge] =
            [
                ("細かく分ける", MeshOperation.Subdivide, null, "選んだ辺を等しく分け、選んだ面は格子に分けます。分ける数はホイールか数字で決めます。"),
                ("ループカット", MeshOperation.CutLoop, PreviewAction.CutLoop, "選んでいる辺の並びに沿って、1 周ぶんの切れ目を入れます。"),
                ("橋渡し", MeshOperation.Bridge, null, "選んだ 2 本の辺の並び（または 2 つの面のまとまり）の間を面でつなぎます。途中の段はホイールで増やせます。"),
                ("辺を回す", MeshOperation.RotateEdge, null, "2 枚の面にはさまれた辺を、となりの頂点へ 1 つずらしてつなぎ直します。"),
                ("辺を逆に回す", MeshOperation.RotateEdgeBack, null, "「辺を回す」と逆の向きにつなぎ直します。"),
                ("辺だけ押し出す", MeshOperation.ExtrudeEdges, null, "選んだ辺を引き出して、帯のような面を作ります。"),
                ("辺を溶かす", MeshOperation.DissolveEdges, null, "穴を空けずに辺だけを取り除きます。"),
                ("", null, null, null),
                ("シャープにする", MeshOperation.MarkSharp, null, "なめらかに見せる面の間でも、この辺は硬い角として見せます。"),
                ("シャープを外す", MeshOperation.ClearSharp, null, "シャープの印を外します。"),
                ("クリース", MeshOperation.Crease, PreviewAction.Crease, "細分化したときに、この辺をどれだけ丸めずに残すかを 0〜1 で決めます。マウスか数字で決めます。"),
                ("", null, null, null),
                ("シームにする", MeshOperation.MarkSeam, null, "展開するときに、この辺で切り開きます。"),
                ("シームを外す", MeshOperation.ClearSeam, null, "シームの印を外します。"),
            ],
            [MeshMenu.Face] =
            [
                ("面を張る", MeshOperation.Fill, PreviewAction.Fill, "選んだ頂点や辺を囲む面を 1 枚作ります。"),
                ("グリッドで埋める", MeshOperation.GridFill, null, "選んだ辺の輪（偶数本）の内側を四角い面の格子で埋めます。幅はホイール、ずらしは Ctrl+ホイールで変えます。"),
                ("三角に割る", MeshOperation.Triangulate, PreviewAction.Triangulate, "選んだ面を三角形に分けます。"),
                ("三角を四角にまとめる", MeshOperation.TrianglesToQuads, PreviewAction.TrianglesToQuads, "となり合う三角形の組を、なるべく形のよい四角形にまとめます。"),
                ("面をばらばらに押し出す", MeshOperation.ExtrudeIndividual, null, "選んだ面を 1 枚ずつ別々に引き出します。"),
                ("面を溶かす", MeshOperation.DissolveFaces, null, "となり合う選んだ面を 1 枚の面にまとめます。"),
                ("", null, null, null),
                ("スムーズで陰影", MeshOperation.ShadeSmooth, null, "選んだ面（選んでいなければ全体）を、角の目立たないなめらかな陰影にします。"),
                ("フラットで陰影", MeshOperation.ShadeFlat, null, "選んだ面（選んでいなければ全体）を、面ごとに平らな陰影にします。"),
                ("自動スムーズで陰影", MeshOperation.ShadeAutoSmooth, null, "なめらかな陰影にしたうえで、きつく折れた辺だけを硬く残します。角度は「色」欄の「スムーズの角度」で変えます。"),
            ],
            [MeshMenu.Normal] =
            [
                ("向きを反転", MeshOperation.FlipNormals, null, "選んだ面の表と裏を入れ替えます。"),
                ("外向きにそろえる", MeshOperation.NormalsOutside, PreviewAction.NormalsOutside, "つながった面の表がそろって外を向くように直します。"),
                ("内向きにそろえる", MeshOperation.NormalsInside, PreviewAction.NormalsInside, "つながった面の表がそろって内を向くように直します。"),
            ],
            [MeshMenu.Add] =
            [
                ("平面", MeshOperation.AddPlane, null, "3D カーソルの位置に四角い面を 1 枚足します。"),
                ("立方体", MeshOperation.AddCube, null, "3D カーソルの位置に立方体を足します。"),
                ("円", MeshOperation.AddCircle, null, "3D カーソルの位置に、面の無い輪（頂点と辺だけ）を足します。"),
                ("UV 球", MeshOperation.AddUvSphere, null, "緯線と経線で分けた球を足します。"),
                ("ICO 球", MeshOperation.AddIcoSphere, null, "三角形だけでできた、どこも同じ細かさの球を足します。"),
                ("円柱", MeshOperation.AddCylinder, null, "3D カーソルの位置に円柱を足します。"),
                ("円錐", MeshOperation.AddCone, null, "3D カーソルの位置に円錐を足します。"),
                ("ドーナツ", MeshOperation.AddTorus, null, "3D カーソルの位置にドーナツを足します。"),
                ("グリッド", MeshOperation.AddGrid, null, "格子に分けた平面を足します。"),
                ("", null, null, null),
                ("ファイルから読み込む…", MeshOperation.Import, null, "obj / gltf / glb / vrm / fbx / pmx の形を、3D カーソルの位置に読み込んで足します。材質も一緒に読み込みます。"),
            ],
            [MeshMenu.Uv] =
            [
                ("展開", MeshOperation.Unwrap, null, "選んだ面（選んでいなければ全体）を、シームで切り開いて、ゆがみが少なくなるように平らに広げます。"),
                ("スマート UV 投影", MeshOperation.SmartProject, null, "向きの近い面どうしをまとめ、まとまりごとに真正面から写します。まとめる角度はマウスか数字で決めます。"),
                ("", null, null, null),
                ("立方体で投影", MeshOperation.CubeProject, null, "面の向きに合わせて、箱の 6 面のどれかから写します。"),
                ("円柱で投影", MeshOperation.CylinderProject, null, "縦の軸のまわりに巻き付けるように写します。"),
                ("球で投影", MeshOperation.SphereProject, null, "地球儀のように、緯度と経度で写します。"),
                ("ビューから投影", MeshOperation.ViewProject, null, "いま見ている向きから、画面に見えているとおりに写します。"),
                ("", null, null, null),
                ("リセット", MeshOperation.ResetUvs, null, "面ごとに画像全体を貼ります。"),
                ("UV の島を並べる", MeshOperation.PackIslands, null, "つながった UV のまとまりを、重ならないように画像の中へ並べ直します。"),
                ("", null, null, null),
                ("シームにする", MeshOperation.MarkSeam, null, "展開するときに、選んだ辺で切り開きます。"),
                ("シームを外す", MeshOperation.ClearSeam, null, "シームの印を外します。"),
            ],
        };

        public IReadOnlyList<MeshMenuEntry> this[string menu]
            => Enum.TryParse<MeshMenu>(menu, out var parsed) ? Entries(parsed) : [];

        public IReadOnlyList<MeshMenuEntry> Entries(MeshMenu menu)
            => [.. Table[menu].Select(one => one.Operation is { } operation
                ? new MeshMenuEntry(one.Header, command, operation.ToString(), one.Key is { } key ? PreviewShortcuts.TextOf(key) : string.Empty, one.Tip, false)
                : new MeshMenuEntry(string.Empty, null, string.Empty, string.Empty, null, true))];
    }
}
