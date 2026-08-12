using System.Collections.Immutable;
using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Integration;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Plugin.Effects;
using YMM43D.Scene3D;

namespace YMM43D.PreviewTool
{
    internal readonly record struct ItemRenderResult(ID2D1Image? Image, Matrix4x4 CameraMatrix)
    {
        public static ItemRenderResult None => new(null, Matrix4x4.Identity);
    }

    internal sealed class ItemRenderPipeline : IDisposable
    {
        private readonly Lock gate = new();
        private readonly Dictionary<IVideoItem, ISource> sources = [];
        private readonly Dictionary<IVideoItem, EffectChain> chains = [];

        private readonly HashSet<IVideoItem> effectsUnsupported = [];

        private readonly Dictionary<IVideoItem, long> sourceRetryAt = [];

        private const long SourceRetryDelayMs = 500;

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

            // ここから先は YMM4 本体の描画を回す。本体の描画スレッドと同じ
            // Direct2D デバイスを使うため、鍵の中で行う。
            // （このメソッドは 3Dプレビューのスレッドから呼ばれる）
            lock (D2DGate.Sync)
                return RenderCore(item, time, environment, needsImage, effects);
        }

        private ItemRenderResult RenderCore(
            IVideoItem item,
            in FrameContext time,
            PreviewEnvironment environment,
            bool needsImage,
            ImmutableList<IVideoEffect> effects)
        {
            var scene = environment.Scene!;
            var sourceDescription = environment.SourceDescription!;

            var description = new TimelineItemSourceDescription(
                sourceDescription, time.Frame, time.Length, item.Layer);

            var image = RenderSource(item, scene, environment, description);
            if (image is null)
                return ItemRenderResult.None;

            if (effects.IsEmpty)
                return new ItemRenderResult(image, Matrix4x4.Identity);

            return ApplyEffects(item, effects, environment, description, image);
        }

        private ID2D1Image? RenderSource(
            IVideoItem item,
            Scene scene,
            PreviewEnvironment environment,
            TimelineItemSourceDescription description)
        {
            if (sourceRetryAt.TryGetValue(item, out var retryAt) && System.Environment.TickCount64 < retryAt)
                return null;

            try
            {
                ISource? source;
                lock (gate)
                {
                    if (!sources.TryGetValue(item, out source))
                    {
                        // ここで作る描画元はプレビュー専用の写しなので、アイテムが
                        // 本来持っているプロバイダーの登録を奪わないようにする。
                        using (Provider3DRegistry.SuppressRegistration())
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
                    {
                        sourceRetryAt.Remove(item);
                        return image;
                    }
                }
            }
            catch
            {
                // 図形のサイズが 0 のときなど、アイテムが一時的に描画できない状態に
                // なることがある。しばらく間を空けてから試し直す。
                sourceRetryAt[item] = System.Environment.TickCount64 + SourceRetryDelayMs;
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

        private static ImmutableList<IVideoEffect> CollectEffects(IVideoItem item)
        {
            if (item.VideoEffects is null)
                return [];

            return [.. item.VideoEffects.Where(e => e.IsEnabled && e is not I3DProvider)];
        }

        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems)
        {
            // 破棄はこの鍵の外で行う。描画元の Dispose は Direct2D の鍵を取ることが
            // あり、鍵を握ったまま呼ぶと取得順序が逆になって詰まる余地ができる。
            var retired = new List<ISource>();

            lock (gate)
            {
                foreach (var item in sources.Keys.Where(k => !aliveItems.Contains(k)).ToArray())
                {
                    if (sources.Remove(item, out var source))
                        retired.Add(source);
                }
            }

            foreach (var source in retired)
                source.Dispose();

            foreach (var item in chains.Keys.Where(k => !aliveItems.Contains(k)).ToArray())
                ReleaseChain(item);

            effectsUnsupported.RemoveWhere(item => !aliveItems.Contains(item));

            foreach (var item in sourceRetryAt.Keys.Where(k => !aliveItems.Contains(k)).ToArray())
                sourceRetryAt.Remove(item);
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
            sourceRetryAt.Clear();
        }

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

            public bool Matches(ImmutableList<IVideoEffect> current)
                => effects.Count == current.Count
                && !effects.Where((effect, i) => !ReferenceEquals(effect, current[i])).Any();

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
