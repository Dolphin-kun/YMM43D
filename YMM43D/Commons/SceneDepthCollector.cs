using System.Numerics;
using YMM43D.Camera;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Commons
{
    /// <summary>
    /// シーン内にある、自分以外の 3D 物体を集めます。
    /// </summary>
    /// <remarks>
    /// YMM4 はアイテムごとに平らな画像を作って重ねるだけで、深度を渡す口がありません。
    /// そこで自分を描く前に他のアイテムの形を深度バッファにだけ埋めます。すると自分の
    /// 画像には「自分が最前面である画素」しか残らず、どの順に重ねても正しい絵になります。
    /// 1枚にまとめないので、レイヤーも不透明度も合成モードも従来どおり効きます。
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
        /// <param name="OwnerPlacement">そのアイテムの 3D 配置行列。拡大率も含みます。</param>
        /// <param name="OwnerScreenPlacement">同じ配置を 2D として表したもの。</param>
        /// <param name="Occluders">自分以外の 3D 物体。</param>
        /// <remarks>
        /// <see cref="OwnerPlacement"/> と <see cref="OwnerScreenPlacement"/> は同じ値から
        /// 同時に作ります。別々に組み立てると、片方だけ拡大率を含むといった食い違いが
        /// 起き、描画先の大きさが拡大率に応じて膨らみます。
        /// </remarks>
        public readonly record struct SceneView(
            IVideoItem? Owner,
            FrameContext OwnerTime,
            Matrix4x4 OwnerPlacement,
            ScreenPlacement OwnerScreenPlacement,
            IReadOnlyList<Occluder> Occluders)
        {
            /// <summary>何も分からなかったことを表す値。</summary>
            public static SceneView None
                => new(null, default, Matrix4x4.Identity, ScreenPlacement.None, []);
        }

        /// <summary>
        /// <paramref name="self"/> 以外の 3D 物体を集めます。
        /// </summary>
        /// <param name="self">
        /// いま描こうとしているプロバイダー。<c>null</c> を渡すと何も集めません。
        /// どのアイテムに属するかは <see cref="TimelineItemSourceDescription.Layer"/> で
        /// 判定するため、この値の同一性には頼っていません。
        /// </param>
        public static SceneView Collect(
            TimelineItemSourceDescription description,
            I3DProvider? self)
        {
            if (self is null)
                return SceneView.None;

            if (TimelineLookup.Find(description) is not { } timeline || timeline.Items is not { } items)
                return SceneView.None;

            var frame = description.TimelinePosition.Frame;
            var fps = description.FPS;

            var alive = items
                .OfType<IVideoItem>()
                .Where(item => !item.IsHidden && ItemPlacement.IsAliveAt(item, frame))
                .Select(item => (Item: item, Time: new FrameContext(frame - item.Frame, item.Length, fps)))
                .ToArray();

            // 自分がどのアイテムに属するかはレイヤー番号で決まる。同じレイヤーの同じ
            // 時刻に複数アイテムは置けないので一意。
            //
            // プロバイダーの参照一致で探してはいけない。YMM4 は同じアイテムに対して
            // 描画元を複数作ることがあり、レジストリに残るのは最後の1つだけになる。
            // 食い違うと自分を見失い、原点に置いてしまう。
            var owner = alive.FirstOrDefault(x => x.Item.Layer == description.Layer);
            if (owner.Item is null)
                return SceneView.None;

            // 深度判定は YMM4 が画像を拡大する前に済んでしまうので、自分も他人も
            // 同じワールド空間に置かないと前後関係が食い違う。
            var ownerPlacement = ItemPlacement.GetWorldMatrix(owner.Item, owner.Time, Matrix4x4.Identity);
            var ownerScreen = ItemPlacement.GetScreenPlacement(owner.Item, owner.Time);

            var occluders = new List<Occluder>();

            foreach (var (item, itemTime) in alive)
            {
                // 自分自身は遮蔽物にしない。ここもプロバイダーではなくアイテムで見る。
                if (ReferenceEquals(item, owner.Item))
                    continue;

                // 他のアイテムに掛かっているカメラ系エフェクトまでは追わない。
                // その値はエフェクトを実行して初めて決まるため、ここでは分からない。
                // 取り込み済みの分は I3DLocalTransform で本人から受け取る。
                var placement = ItemPlacement.GetWorldMatrix(item, itemTime, Matrix4x4.Identity);

                foreach (var provider in FindProviders(item))
                    occluders.Add(new Occluder(provider, GetLocalMatrix(provider) * placement, itemTime));
            }

            return new SceneView(owner.Item, owner.Time, ownerPlacement, ownerScreen, occluders);
        }

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

        private static Matrix4x4 GetLocalMatrix(I3DProvider provider)
        {
            if (provider is I3DLocalTransform transform && transform.TryGetLocalMatrix(out var matrix))
                return matrix;

            // 実寸を自分で扱うプロバイダーには掛けない。掛けると遮蔽物としての形が
            // 本人の描く形より大きくなり、隠れなくてよいところまで隠れる。
            if (provider is I3DSizeProvider sizeProvider
                && sizeProvider.ScalesToInputSize
                && sizeProvider.TryGetSize(out var size, out var offset))
            {
                return WorldScale.CreateSizeMatrix(size, offset + size / 2f);
            }

            return Matrix4x4.Identity;
        }
    }
}
