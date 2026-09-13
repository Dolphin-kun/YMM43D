using System.Collections.Immutable;
using Vortice.Direct2D1;
using YMM43D.Player;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal readonly record struct ItemRenderResult(ID2D1Image? Image, DrawDescription Draw);

    internal sealed class ItemRenderPipeline : IDisposable
    {
        private readonly Lock gate = new();
        private readonly Dictionary<IVideoItem, ISource> sources = [];
        private readonly Dictionary<(IVideoItem Item, I3DProvider Provider), EffectChain> chains = [];

        private readonly Dictionary<IVideoItem, long> sourceRetryAt = [];

        private readonly Dictionary<IVideoItem, long> effectRetryAt = [];

        private const long RetryDelayMs = 500;

        public ItemRenderResult Render(
            IVideoItem item,
            in FrameContext time,
            PreviewEnvironment environment,
            bool needsImage,
            I3DProvider provider,
            ImmutableList<IVideoEffect> effects,
            DrawDescription seed)
        {
            if (environment.Scene is null || environment.SourceDescription is null)
                return new ItemRenderResult(null, seed);

            if (!needsImage && effects.IsEmpty)
                return new ItemRenderResult(null, seed);

            lock (D2DGate.Sync)
                return RenderCore(item, time, environment, effects, provider, seed);
        }

        private ItemRenderResult RenderCore(
            IVideoItem item,
            in FrameContext time,
            PreviewEnvironment environment,
            ImmutableList<IVideoEffect> effects,
            I3DProvider provider,
            DrawDescription seed)
        {
            var scene = environment.Scene!;
            var sourceDescription = environment.SourceDescription!;

            var description = new TimelineItemSourceDescription(
                sourceDescription, time.Frame, time.Length, item.Layer);

            var image = RenderSource(item, scene, environment, description);
            if (image is null)
                return new ItemRenderResult(null, seed);

            if (effects.IsEmpty)
                return new ItemRenderResult(image, seed);

            return ApplyEffects(item, provider, effects, environment, description, image, seed);
        }

        private ID2D1Image? RenderSource(
            IVideoItem item,
            Scene scene,
            PreviewEnvironment environment,
            TimelineItemSourceDescription description)
        {
            if (IsWaiting(sourceRetryAt, item))
                return null;

            try
            {
                ISource? source;
                lock (gate)
                {
                    if (!sources.TryGetValue(item, out source))
                    {
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
                sourceRetryAt[item] = System.Environment.TickCount64 + RetryDelayMs;
            }

            return null;
        }

        private ItemRenderResult ApplyEffects(
            IVideoItem item,
            I3DProvider provider,
            ImmutableList<IVideoEffect> effects,
            PreviewEnvironment environment,
            TimelineItemSourceDescription description,
            ID2D1Image sourceImage,
            DrawDescription seed)
        {
            if (IsWaiting(effectRetryAt, item))
                return new ItemRenderResult(sourceImage, seed);

            var key = (item, provider);

            try
            {
                if (!chains.TryGetValue(key, out var chain) || !chain.Matches(effects))
                {
                    ReleaseChain(key);

                    using (Provider3DRegistry.SuppressRegistration())
                        chain = chains[key] = new EffectChain(effects, environment.Devices);
                }

                var applied = chain.Apply(sourceImage, description, seed);
                effectRetryAt.Remove(item);
                return applied;
            }
            catch
            {
                ReleaseChain(key);
                effectRetryAt[item] = System.Environment.TickCount64 + RetryDelayMs;
                return new ItemRenderResult(sourceImage, seed);
            }
        }

        private static bool IsWaiting(Dictionary<IVideoItem, long> retryAt, IVideoItem item)
            => retryAt.TryGetValue(item, out var at) && System.Environment.TickCount64 < at;

        public void RetainOnly(IReadOnlySet<IVideoItem> aliveItems)
        {
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

            foreach (var key in chains.Keys.Where(k => !aliveItems.Contains(k.Item)).ToArray())
                ReleaseChain(key);

            Prune(sourceRetryAt, aliveItems);
            Prune(effectRetryAt, aliveItems);
        }

        private static void Prune(Dictionary<IVideoItem, long> retryAt, IReadOnlySet<IVideoItem> aliveItems)
        {
            foreach (var item in retryAt.Keys.Where(k => !aliveItems.Contains(k)).ToArray())
                retryAt.Remove(item);
        }

        private void ReleaseChain((IVideoItem Item, I3DProvider Provider) key)
        {
            if (chains.Remove(key, out var chain))
                chain.Dispose();
        }

        public void Clear()
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
            effectRetryAt.Clear();
            sourceRetryAt.Clear();
        }

        public void Dispose() => Clear();

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
            {
                if (effects.Count != current.Count)
                    return false;

                for (var i = 0; i < effects.Count; i++)
                {
                    if (!ReferenceEquals(effects[i], current[i]))
                        return false;
                }

                return true;
            }

            public ItemRenderResult Apply(
                ID2D1Image input, TimelineItemSourceDescription description, DrawDescription seed)
            {
                var draw = seed;
                var image = input;

                foreach (var processor in processors)
                {
                    processor.SetInput(image);

                    draw = processor.Update(new EffectDescription(
                        description, draw, inputIndex: 0, inputCount: 1, groupIndex: 0, groupCount: 1));

                    image = processor.Output;
                }

                return new ItemRenderResult(image, draw);
            }

            public void Dispose()
            {
                foreach (var processor in processors)
                {
                    try { processor.ClearInput(); } catch { }
                    processor.Dispose();
                }

                processors.Clear();
            }
        }
    }
}
