using System.Collections.Immutable;
using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Plugin.Effects;
using YMM43D.Scene3D;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// アイテムの 2D 描画結果と、エフェクトが生み出す 3D 変換行列。
    /// </summary>
    /// <param name="Image">エフェクト適用後の画像。取得できなかった場合は <c>null</c>。</param>
    /// <param name="CameraMatrix">エフェクトが設定した変換行列。無ければ単位行列。</param>
    internal readonly record struct ItemRenderResult(ID2D1Image? Image, Matrix4x4 CameraMatrix)
    {
        public static ItemRenderResult None => new(null, Matrix4x4.Identity);
    }

    /// <summary>
    /// アイテムの描画元とエフェクトチェーンを、YMM4 本体と同じ手順で駆動します。
    /// </summary>
    /// <remarks>
    /// <para>
    /// YMM4 の標準エフェクト（3D回転など）は <see cref="DrawDescription.Camera"/> に
    /// 変換行列を書き込みます。3Dプレビューでもこれを反映させたいのですが、合成後の
    /// <see cref="DrawDescription"/> を持つ <c>EffectedItemSource</c> は YMM4 の内部
    /// クラスで、公開インターフェース経由では取り出せません。
    /// </para>
    /// <para>
    /// そこで YMM4 が内部で行っているのと同じ連鎖を公開 API だけで組み立てます。
    /// <c>SetInput</c> → <c>Update</c> → <c>Output</c> を順に繋いでいき、最後の
    /// <see cref="DrawDescription.Camera"/> を読みます。必要な部品はすべて公開されて
    /// いるため、リフレクションは使いません。
    /// </para>
    /// <para>
    /// 副産物として、エフェクト適用後の画像が得られます。これを 3D 空間に貼ることで、
    /// プレビューにもモザイクや色調補正といった 2D エフェクトの結果が反映されます。
    /// </para>
    /// <para>
    /// この連鎖は YMM4 本体が動かしているものとは別物なので、エフェクトは 1 フレームに
    /// 2 回評価されることになります。プレビュー用途では許容範囲と判断しています。
    /// </para>
    /// </remarks>
    internal sealed class ItemRenderPipeline : IDisposable
    {
        private readonly Lock gate = new();
        private readonly Dictionary<IVideoItem, ISource> sources = [];
        private readonly Dictionary<IVideoItem, EffectChain> chains = [];

        /// <summary>
        /// エフェクト連鎖の駆動に失敗したアイテム。毎フレーム作り直しては失敗するのを
        /// 避けるため、一度失敗したら以降はエフェクトを通さない。
        /// </summary>
        private readonly HashSet<IVideoItem> effectsUnsupported = [];

        /// <summary>
        /// アイテムを描画し、画像と変換行列を返します。
        /// </summary>
        /// <param name="needsImage">
        /// <c>false</c> の場合、画像を必要としないプロバイダー向けに、変換行列だけを
        /// 求めます。エフェクトが無ければ何も行いません。
        /// </param>
        public ItemRenderResult Render(
            IVideoItem item,
            in FrameContext time,
            PreviewEnvironment environment,
            bool needsImage)
        {
            if (environment.Scene is not { } scene || environment.SourceDescription is not { } sourceDescription)
                return ItemRenderResult.None;

            var effects = CollectEffects(item);

            // 画像も要らず、変換を生むエフェクトも無いなら、描画元を作る必要はない。
            if (!needsImage && effects.IsEmpty)
                return ItemRenderResult.None;

            var description = new TimelineItemSourceDescription(
                sourceDescription, time.Frame, time.Length, item.Layer);

            var image = RenderSource(item, scene, environment, description);
            if (image is null)
                return ItemRenderResult.None;

            if (effects.IsEmpty)
                return new ItemRenderResult(image, Matrix4x4.Identity);

            return ApplyEffects(item, effects, environment, description, image);
        }

        /// <summary>
        /// アイテム本来の（エフェクトを通す前の）画像を得ます。
        /// </summary>
        private ID2D1Image? RenderSource(
            IVideoItem item,
            Scene scene,
            PreviewEnvironment environment,
            TimelineItemSourceDescription description)
        {
            try
            {
                ISource? source;
                lock (gate)
                {
                    if (!sources.TryGetValue(item, out source))
                    {
                        source = item.CreateVideoSource(environment.Devices, scene);
                        if (source is null)
                            return null;
                        sources[item] = source;
                    }
                }

                source.Update(description);

                foreach (var output in source.Outputs ?? [])
                {
                    if (output?.Output is { } image)
                        return image;
                }
            }
            catch
            {
                // アイテムによっては描画元を作れないことがある。
            }

            return null;
        }

        private ItemRenderResult ApplyEffects(
            IVideoItem item,
            ImmutableList<IVideoEffect> effects,
            PreviewEnvironment environment,
            TimelineItemSourceDescription description,
            ID2D1Image sourceImage)
        {
            if (effectsUnsupported.Contains(item))
                return new ItemRenderResult(sourceImage, Matrix4x4.Identity);

            try
            {
                if (!chains.TryGetValue(item, out var chain) || !chain.Matches(effects))
                {
                    ReleaseChain(item);
                    chain = chains[item] = new EffectChain(effects, environment.Devices);
                }

                return chain.Apply(sourceImage, description);
            }
            catch
            {
                // YMM4 本来の駆動を経ていない状態に耐えられないエフェクトがある。
                // このアイテムは以降エフェクトを通さず、素の画像で表示する。
                ReleaseChain(item);
                effectsUnsupported.Add(item);
                return new ItemRenderResult(sourceImage, Matrix4x4.Identity);
            }
        }

        /// <summary>
        /// 連鎖に通すエフェクトを選びます。
        /// </summary>
        /// <remarks>
        /// このライブラリ自身の 3D エフェクトは対象外にします。それらのプロセッサは
        /// エフェクト本体と結び付いており（<see cref="VideoEffect3DBase"/>）、ここで
        /// 作り直すと本物のプロセッサがこの場限りのコピーに差し替わってしまいます。
        /// これらの描画は <see cref="I3DProvider"/> として直接行われるため、
        /// 連鎖に通す必要もありません。
        /// </remarks>
        private static ImmutableList<IVideoEffect> CollectEffects(IVideoItem item)
        {
            if (item.VideoEffects is null)
                return [];

            return [.. item.VideoEffects.Where(e => e.IsEnabled && e is not I3DProvider)];
        }

        /// <summary>
        /// 表示対象でなくなったアイテムの資源を解放します。
        /// </summary>
        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems)
        {
            lock (gate)
            {
                foreach (var item in sources.Keys.Where(k => !aliveItems.Contains(k)).ToArray())
                {
                    if (sources.Remove(item, out var source))
                        source.Dispose();
                }
            }

            foreach (var item in chains.Keys.Where(k => !aliveItems.Contains(k)).ToArray())
                ReleaseChain(item);

            effectsUnsupported.RemoveWhere(item => !aliveItems.Contains(item));
        }

        private void ReleaseChain(IVideoItem item)
        {
            if (chains.Remove(item, out var chain))
                chain.Dispose();
        }

        public void Dispose()
        {
            lock (gate)
            {
                foreach (var source in sources.Values)
                    source.Dispose();
                sources.Clear();
            }

            foreach (var chain in chains.Values)
                chain.Dispose();
            chains.Clear();
            effectsUnsupported.Clear();
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

            /// <summary>
            /// 入力画像にエフェクトを順に適用します。
            /// </summary>
            public ItemRenderResult Apply(ID2D1Image input, TimelineItemSourceDescription description)
            {
                var draw = CreateInitialDrawDescription();
                var image = input;

                foreach (var processor in processors)
                {
                    // YMM4 本体と同じ順序。入力を繋いでから Update を呼ばないと、
                    // エフェクトが組む D2D グラフが未完成のまま評価されてしまう。
                    processor.SetInput(image);

                    // 入力は1つ、グループ分けもしない単純な構成として評価する。
                    draw = processor.Update(new EffectDescription(
                        description, draw, inputIndex: 0, inputCount: 1, groupIndex: 0, groupCount: 1));

                    image = processor.Output;
                }

                return new ItemRenderResult(image, draw.Camera);
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
                {
                    // 入力を握ったまま破棄すると、参照先の画像より長生きしてしまう。
                    try { processor.ClearInput(); } catch { }
                    processor.Dispose();
                }

                processors.Clear();
            }
        }
    }
}
