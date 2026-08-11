using System.Numerics;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Integration
{
    /// <summary>
    /// シーン内にある、自分以外の 3D 物体を集めます。
    /// </summary>
    /// <remarks>
    /// <para>
    /// YMM4 はアイテムごとに平らな画像を作り、レイヤー順に重ねます。深度を渡す口が
    /// ないため、そのままではアイテムをまたいだ前後関係を表現できません。
    /// </para>
    /// <para>
    /// そこで、自分を描く前に他のアイテムの形を深度バッファにだけ埋めます。すると
    /// 自分の画像には「自分が最前面である画素」しか残らず、どの順に重ねても正しい絵に
    /// なります。1枚にまとめる必要がないので、レイヤーも不透明度も合成モードも
    /// アイテムごとに従来どおり効きます。
    /// </para>
    /// </remarks>
    public static class SceneDepthCollector
    {
        /// <summary>
        /// 深度に埋める 3D 物体1つ分。
        /// </summary>
        /// <param name="Provider">描画を行うプロバイダー。</param>
        /// <param name="World">この物体のワールド行列。</param>
        /// <param name="Time">この物体のアイテム内での時間位置。</param>
        public readonly record struct Occluder(I3DProvider Provider, Matrix4x4 World, FrameContext Time);

        /// <summary>
        /// シーンを見渡した結果。
        /// </summary>
        /// <param name="Owner">自分が属するアイテム。見つからなければ <c>null</c>。</param>
        /// <param name="OwnerTime">そのアイテム内での時間位置。</param>
        /// <param name="OwnerPlacement">そのアイテムの配置行列。</param>
        /// <param name="Occluders">自分以外の 3D 物体。</param>
        public readonly record struct SceneView(
            IVideoItem? Owner,
            FrameContext OwnerTime,
            Matrix4x4 OwnerPlacement,
            IReadOnlyList<Occluder> Occluders)
        {
            /// <summary>何も分からなかったことを表す値。</summary>
            public static SceneView None => new(null, default, Matrix4x4.Identity, []);
        }

        /// <summary>
        /// <paramref name="self"/> 以外の 3D 物体を集めます。
        /// </summary>
        /// <param name="description">YMM4 から渡された描画要求。</param>
        /// <param name="self">
        /// いま描こうとしているプロバイダー。<c>null</c> を渡すと何も集めません。
        /// どのアイテムに属するかは <see cref="TimelineItemSourceDescription.Layer"/> で
        /// 判定するため、この値の同一性には頼っていません。
        /// </param>
        /// <remarks>
        /// すべてワールド座標のまま返します。自分の位置は呼び出し側が
        /// <see cref="SceneView.OwnerPlacement"/> から知り、YMM4 が後から画像に掛ける
        /// 2D 配置を打ち消すのに使います。
        /// </remarks>
        public static SceneView Collect(
            TimelineItemSourceDescription description,
            I3DProvider? self)
        {
            if (self is null)
                return SceneView.None;

            if (FindTimeline(description) is not { } timeline || timeline.Items is not { } items)
                return SceneView.None;

            var frame = description.TimelinePosition.Frame;
            var fps = description.FPS;

            var alive = items
                .OfType<IVideoItem>()
                .Where(item => !item.IsHidden && ItemPlacement.IsAliveAt(item, frame))
                .Select(item => (Item: item, Time: new FrameContext(frame - item.Frame, item.Length, fps)))
                .ToArray();

            // 自分がどのアイテムに属しているかは、描画要求のレイヤー番号で分かる。
            // 同じレイヤーに同じ時刻の複数アイテムは置けないので、これで一意に決まる。
            //
            // プロバイダーの参照一致で探してはいけない。YMM4 は同じアイテムに対して
            // 描画元を複数作ることがあり（プレビュー用と出力用など）、レジストリに
            // 残るのは最後に作られた1つだけになる。描画している当人と登録されている
            // ものが食い違うと自分を見失い、位置を単位行列のまま原点に置いてしまう。
            var owner = alive.FirstOrDefault(x => x.Item.Layer == description.Layer);
            if (owner.Item is null)
                return SceneView.None;

            // 自分の配置に拡大率は入れない。YMM4 が出来上がった画像に掛けるため。
            var ownerPlacement = ItemPlacement.GetWorldMatrix(
                owner.Item, owner.Time, Matrix4x4.Identity, includeZoom: false);

            var unzoom = GetZoomCancel(owner.Item, owner.Time, ownerPlacement.Translation);

            var occluders = new List<Occluder>();

            foreach (var (item, itemTime) in alive)
            {
                // 自分自身は遮蔽物にしない。ここもプロバイダーではなくアイテムで見る。
                if (ReferenceEquals(item, owner.Item))
                    continue;

                // 遮蔽物は他アイテムの見た目そのものなので、拡大率も含める。含めないと、
                // 縮小したアイテムが等倍のままの大きさで手前をくり抜いてしまう。
                //
                // 他のアイテムに掛かっているカメラ系エフェクトまでは追わない。
                // その値はエフェクトを実行して初めて決まるため、ここでは分からない。
                // 取り込み済みの分は I3DLocalTransform で本人から受け取る。
                var placement = ItemPlacement.GetWorldMatrix(item, itemTime, Matrix4x4.Identity);

                foreach (var provider in FindProviders(item))
                    occluders.Add(new Occluder(provider, GetLocalMatrix(provider) * placement * unzoom, itemTime));
            }

            return new SceneView(owner.Item, owner.Time, ownerPlacement, occluders);
        }

        /// <summary>
        /// 自分に掛かる拡大率を、遮蔽物の側で先に打ち消す変換を作ります。
        /// </summary>
        /// <remarks>
        /// <para>
        /// YMM4 は出来上がった画像全体に拡大率を掛けます。その画像には深度用に埋めた
        /// 他アイテムの形も含まれるので、何もしないと遮蔽物まで自分の拡大率で
        /// 拡大・縮小されてしまいます。そこで自分の原点を中心にあらかじめ
        /// 1/拡大率 倍しておき、後から掛かる拡大と相殺させます。
        /// </para>
        /// <para>
        /// 打ち消しを画像の引き伸ばしで行うと、縮小するほど中間画像が巨大になって
        /// 破綻します。深度に埋める形の側を動かす分には、画像の大きさは変わりません。
        /// </para>
        /// <para>
        /// ただし相殺できるのは大きさだけです。YMM4 の拡大は平らな画像に対する操作
        /// なので、遠近の効き方までは一致しません。拡大率が極端な場合や、遮蔽物が
        /// 自分から大きく離れている場合はわずかにずれます。
        /// </para>
        /// </remarks>
        private static Matrix4x4 GetZoomCancel(IVideoItem owner, in FrameContext time, Vector3 origin)
        {
            var zoom = owner.Zoom.GetFloat(time) / 100f;

            if (!float.IsFinite(zoom) || zoom <= 0f || zoom == 1f)
                return Matrix4x4.Identity;

            return Matrix4x4.CreateTranslation(-origin)
                 * Matrix4x4.CreateScale(1f / zoom)
                 * Matrix4x4.CreateTranslation(origin);
        }

        /// <summary>
        /// 描画要求から、いま描かれているシーンのタイムラインを探します。
        /// </summary>
        /// <remarks>
        /// <see cref="TimelineSourceDescription.Scenes"/> の要素は <c>ISceneInfo</c> として
        /// 渡されますが、実体は <see cref="Scene"/> でタイムラインを持っています。
        /// 型が違えば何も返しません。
        /// </remarks>
        private static Timeline? FindTimeline(TimelineSourceDescription description)
        {
            foreach (var info in description.Scenes ?? [])
            {
                if (info is Scene scene && scene.ID == description.SceneId)
                    return scene.Timeline;
            }

            return null;
        }

        /// <summary>
        /// アイテムを 3D 描画できるプロバイダーを集めます。
        /// </summary>
        /// <remarks>
        /// 3Dプレビューと同じ規則です。アイテム自身が 3D 描画を持つ場合は、
        /// エフェクト側の 3D 描画は使いません。
        /// </remarks>
        private static IEnumerable<I3DProvider> FindProviders(IVideoItem item)
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

            return providers.Distinct();
        }

        /// <summary>
        /// アイテムの配置を除いた、プロバイダー自身の変換を求めます。
        /// </summary>
        /// <remarks>
        /// エフェクトは <c>DrawDescription.Camera</c> を取り込んでいることがあり、
        /// その値は実寸からは組み立て直せません。連鎖を辿り直すのは高くつくので、
        /// 使った変換を本人に答えてもらいます。答えられない場合だけ実寸から作ります。
        /// </remarks>
        private static Matrix4x4 GetLocalMatrix(I3DProvider provider)
        {
            if (provider is I3DLocalTransform transform && transform.TryGetLocalMatrix(out var matrix))
                return matrix;

            if (provider is I3DSizeProvider sizeProvider
                && sizeProvider.TryGetSize(out var size, out var offset))
            {
                return WorldScale.CreateSizeMatrix(size, offset + size / 2f);
            }

            return Matrix4x4.Identity;
        }
    }
}
