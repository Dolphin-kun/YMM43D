using System.Collections.Immutable;
using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YMM43D.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// アイテムに掛かっている映像エフェクトが生み出す 3D 変換行列を求めます。
    /// </summary>
    /// <remarks>
    /// <para>
    /// YMM4 の標準エフェクト（3D回転など）は <see cref="DrawDescription.Camera"/> に
    /// 変換行列を書き込みます。3Dプレビューでもこの変換を反映させたいのですが、
    /// 合成後の <see cref="DrawDescription"/> を保持している <c>EffectedItemSource</c> は
    /// YMM4 の内部クラスで、公開インターフェース経由では取り出せません。
    /// </para>
    /// <para>
    /// そこで YMM4 が内部で行っているのと同じことを公開 API だけで再現します。
    /// アイテムの映像エフェクト一覧からプロセッサを作り、順に
    /// <see cref="IVideoEffectProcessor.Update"/> を呼んで <see cref="DrawDescription"/> を
    /// 積み上げ、最終的な <see cref="DrawDescription.Camera"/> を読み取ります。
    /// 必要な部品はすべて公開されているため、リフレクションは使いません。
    /// </para>
    /// </remarks>
    internal sealed class ItemTransformResolver : IDisposable
    {
        private readonly Dictionary<IVideoItem, EffectChain> chains = [];

        /// <summary>
        /// 変換の取得に失敗したアイテム。毎フレーム作り直しては失敗するのを避けるため、
        /// 一度失敗したら以降は試みない。
        /// </summary>
        private readonly HashSet<IVideoItem> unsupported = [];

        /// <summary>
        /// アイテムのエフェクトが生み出す変換行列を返します。
        /// エフェクトが無い場合や取得に失敗した場合は単位行列を返します。
        /// </summary>
        public Matrix4x4 GetCameraMatrix(
            IVideoItem item,
            IGraphicsDevicesAndContext devices,
            TimelineItemSourceDescription description)
        {
            if (unsupported.Contains(item))
                return Matrix4x4.Identity;

            var effects = CollectEffects(item);
            if (effects.IsEmpty)
            {
                Release(item);
                return Matrix4x4.Identity;
            }

            try
            {
                if (!chains.TryGetValue(item, out var chain) || !chain.Matches(effects))
                {
                    Release(item);
                    chain = chains[item] = new EffectChain(effects, devices);
                }

                return chain.Resolve(description);
            }
            catch
            {
                // YMM4 本来の呼び出し順を経ていない状態での Update に耐えられない
                // エフェクトがある。プレビューでの変換が反映されないだけなので、
                // このアイテムは以降あきらめる。毎フレーム作り直すと例外が出続け、
                // プロセッサの生成と破棄も繰り返すことになる。
                Release(item);
                unsupported.Add(item);
                return Matrix4x4.Identity;
            }
        }

        /// <summary>
        /// 変換の解決に使うエフェクトを選びます。
        /// </summary>
        /// <remarks>
        /// このライブラリ自身の 3D エフェクトは対象外にします。それらのプロセッサは
        /// エフェクト本体と結び付いており（<see cref="Plugin.VideoEffect3DBase"/>）、
        /// ここで作り直すと本物のプロセッサが使い捨てのコピーに差し替わってしまいます。
        /// またこれらの変換は <see cref="I3DProvider"/> として直接描画されるため、
        /// DrawDescription 経由で取り出す必要もありません。
        /// </remarks>
        private static ImmutableList<IVideoEffect> CollectEffects(IVideoItem item)
        {
            if (item.VideoEffects is null)
                return [];

            return [.. item.VideoEffects.Where(e => e.IsEnabled && e is not I3DProvider)];
        }

        /// <summary>
        /// 表示対象でなくなったアイテムのプロセッサを解放します。
        /// </summary>
        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems)
        {
            foreach (var item in chains.Keys.Where(k => !aliveItems.Contains(k)).ToArray())
                Release(item);

            unsupported.RemoveWhere(item => !aliveItems.Contains(item));
        }

        private void Release(IVideoItem item)
        {
            if (chains.Remove(item, out var chain))
                chain.Dispose();
        }

        public void Dispose()
        {
            foreach (var chain in chains.Values)
                chain.Dispose();
            chains.Clear();
        }

        /// <summary>
        /// 1つのアイテムに対応する、エフェクトプロセッサの並び。
        /// </summary>
        private sealed class EffectChain : IDisposable
        {
            private readonly ImmutableList<IVideoEffect> effects;
            private readonly List<IVideoEffectProcessor> processors = [];

            public EffectChain(ImmutableList<IVideoEffect> effects, IGraphicsDevicesAndContext devices)
            {
                this.effects = effects;
                foreach (var effect in effects)
                    processors.Add(effect.CreateVideoEffect(devices));
            }

            /// <summary>
            /// エフェクトの構成が変わっていないかを調べます。
            /// </summary>
            public bool Matches(ImmutableList<IVideoEffect> current)
                => effects.Count == current.Count
                && !effects.Where((effect, i) => !ReferenceEquals(effect, current[i])).Any();

            public Matrix4x4 Resolve(TimelineItemSourceDescription description)
            {
                var draw = CreateInitialDrawDescription();

                foreach (var processor in processors)
                {
                    // 入力は1つ、グループ分けもしない単純な構成として評価する。
                    // 求めたいのは変換行列だけなので、これで足りる。
                    draw = processor.Update(new EffectDescription(
                        description, draw, inputIndex: 0, inputCount: 1, groupIndex: 0, groupCount: 1));
                }

                return draw.Camera;
            }

            /// <summary>
            /// 変換が何も掛かっていない状態の <see cref="DrawDescription"/>。
            /// </summary>
            private static DrawDescription CreateInitialDrawDescription() => new(
                Draw: Vector3.Zero,
                CenterPoint: Vector2.Zero,
                Zoom: Vector2.One,
                Rotation: Vector3.Zero,
                Camera: Matrix4x4.Identity,
                ZoomInterpolationMode: InterpolationMode.Linear,
                Opacity: 1.0,
                Invert: false,
                Controllers: []);

            public void Dispose()
            {
                foreach (var processor in processors)
                    processor.Dispose();
                processors.Clear();
            }
        }
    }
}
