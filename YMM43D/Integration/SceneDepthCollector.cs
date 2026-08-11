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
        /// <param name="World">自分を原点としたときの、この物体のワールド行列。</param>
        /// <param name="Time">この物体のアイテム内での時間位置。</param>
        public readonly record struct Occluder(I3DProvider Provider, Matrix4x4 World, FrameContext Time);

        /// <summary>
        /// <paramref name="self"/> 以外の 3D 物体を集めます。
        /// </summary>
        /// <param name="description">YMM4 から渡された描画要求。</param>
        /// <param name="self">
        /// いま描こうとしているプロバイダー。3Dプレビューがアイテムから辿るのと同じ
        /// ものを渡してください（エフェクトの場合はプロセッサではなくエフェクト本体）。
        /// </param>
        /// <remarks>
        /// 出力経路では、自分はアイテムの位置を打ち消した状態（原点）で描かれ、
        /// 位置は YMM4 が後から画像に対して掛けます。そのため他の物体も、自分の
        /// アイテム位置を打ち消した座標系に移して返します。
        /// </remarks>
        public static IReadOnlyList<Occluder> Collect(
            TimelineItemSourceDescription description,
            I3DProvider? self)
        {
            if (self is null)
                return [];

            if (FindTimeline(description) is not { } timeline || timeline.Items is not { } items)
                return [];

            var frame = description.TimelinePosition.Frame;
            var fps = description.FPS;

            var alive = items
                .OfType<IVideoItem>()
                .Where(item => !item.IsHidden && ItemPlacement.IsAliveAt(item, frame))
                .Select(item => (Item: item, Time: new FrameContext(frame - item.Frame, item.Length, fps)))
                .ToArray();

            // 自分がどのアイテムに属しているかが分からないと、他の物体を自分から見た
            // 位置に置けない。見つからない場合は、誤った隠れ方をするより何もしない。
            var owner = alive.FirstOrDefault(x => FindProviders(x.Item).Contains(self));
            if (owner.Item is null)
                return [];

            var selfPlacement = ItemPlacement.GetWorldMatrix(owner.Item, owner.Time, Matrix4x4.Identity);
            if (!Matrix4x4.Invert(selfPlacement, out var toSelfSpace))
                return [];

            var occluders = new List<Occluder>();

            foreach (var (item, itemTime) in alive)
            {
                // 他のアイテムに掛かっているカメラ系エフェクトまでは追わない。
                // その値はエフェクトを実行して初めて決まるため、ここでは分からない。
                var placement = ItemPlacement.GetWorldMatrix(item, itemTime, Matrix4x4.Identity);

                foreach (var provider in FindProviders(item))
                {
                    if (ReferenceEquals(provider, self))
                        continue;

                    var local = GetLocalMatrix(provider);
                    occluders.Add(new Occluder(provider, local * placement * toSelfSpace, itemTime));
                }
            }

            return occluders;
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
