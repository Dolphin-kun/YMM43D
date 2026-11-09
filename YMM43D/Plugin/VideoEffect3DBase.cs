using System.ComponentModel;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM43D.Plugin
{
    public abstract class VideoEffect3DBase
        : VideoEffectBase, I3DVideoEffect, ICameraSync, I3DSizeProvider, I3DLocalTransform, I3DBounds, I3DInstances
    {
        private static readonly object UnknownDevices = new();

        private readonly CameraSync cameraSync = new();
        private readonly Lock instanceGate = new();
        private readonly Dictionary<I3DProvider, object> attached = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, Channel> channels = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<I3DProvider, (int Layer, int Input)> placedAt = new(ReferenceEqualityComparer.Instance);

        private sealed class Channel
        {
            public I3DProvider? Latest { get; set; }

            public I3DProvider?[] Inputs { get; set; } = [];
        }

        protected VideoEffect3DBase()
        {
            cameraSync.Changed += () => OnPropertyChanged(nameof(CameraSyncAnimation));
        }

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public Animation CameraSyncAnimation => cameraSync.Value;

        protected I3DProvider? Processor { get; private set; }

        public virtual bool RequiresMappedTexture => false;

        public void TouchCameraSync() => cameraSync.TouchCameraSync();

        protected TProcessor AttachProcessor<TProcessor>(TProcessor processor) where TProcessor : I3DProvider
        {
            if (Provider3DRegistry.IsSuppressed)
                return processor;

            Processor = processor;

            lock (instanceGate)
            {
                var key = SourceKey.Of(processor is VideoEffect3DProcessorBase based ? based.SourceDevices : null) ?? UnknownDevices;

                attached[processor] = key;
                ChannelFor(key).Latest = processor;
            }

            return processor;
        }

        public void DetachProcessor(I3DProvider processor)
        {
            if (ReferenceEquals(Processor, processor))
                Processor = null;

            lock (instanceGate)
            {
                if (attached.Remove(processor, out var key) && channels.TryGetValue(key, out var channel))
                {
                    if (ReferenceEquals(channel.Latest, processor))
                        channel.Latest = null;

                    for (var i = 0; i < channel.Inputs.Length; i++)
                    {
                        if (ReferenceEquals(channel.Inputs[i], processor))
                            channel.Inputs[i] = null;
                    }

                    if (channel.Latest is null && channel.Inputs.All(input => input is null))
                        channels.Remove(key);
                }

                placedAt.Remove(processor);
            }
        }

        internal void ReportInput(I3DProvider processor, int layer, int index, int count)
        {
            lock (instanceGate)
            {
                if (!attached.TryGetValue(processor, out var key) || index < 0 || index >= count)
                    return;

                Processor = processor;
                placedAt[processor] = (layer, index);

                var channel = ChannelFor(key);
                channel.Latest = processor;

                if (channel.Inputs.Length != count)
                    channel.Inputs = new I3DProvider?[count];

                channel.Inputs[index] = processor;
            }
        }

        public IReadOnlyList<I3DProvider> GetInstancesAt(int layer, IGraphicsDevicesAndContext? devices = null)
        {
            lock (instanceGate)
            {
                var key = ResolveKey(devices);

                return
                [
                    .. placedAt
                        .Where(pair => pair.Value.Layer == layer && ReferenceEquals(attached[pair.Key], key))
                        .OrderBy(pair => pair.Value.Input)
                        .Select(pair => pair.Key),
                ];
            }
        }

        public IReadOnlyList<I3DProvider> GetInstances() => GetInstances(null);

        public IReadOnlyList<I3DProvider> GetInstances(IGraphicsDevicesAndContext? devices)
        {
            lock (instanceGate)
            {
                var inputs = channels.TryGetValue(ResolveKey(devices), out var channel) ? channel.Inputs : [];

                if (inputs.Length < 2)
                    return [this];

                I3DProvider[] found = [.. inputs.OfType<I3DProvider>()];

                return found.Length > 0 ? found : [this];
            }
        }

        public I3DProvider? GetInstance(int inputIndex, IGraphicsDevicesAndContext? devices = null)
        {
            lock (instanceGate)
            {
                if (!channels.TryGetValue(ResolveKey(devices), out var channel))
                    return Processor;

                return inputIndex >= 0 && inputIndex < channel.Inputs.Length && channel.Inputs[inputIndex] is { } input
                    ? input
                    : channel.Latest ?? Processor;
            }
        }

        private I3DProvider? ProcessorFor(IGraphicsDevicesAndContext? devices)
        {
            if (devices is null)
                return Processor;

            lock (instanceGate)
            {
                return SourceKey.Of(devices) is { } key && channels.TryGetValue(key, out var channel) && channel.Latest is { } latest
                    ? latest
                    : Processor;
            }
        }

        private object ResolveKey(IGraphicsDevicesAndContext? devices)
        {
            if (SourceKey.Of(devices) is { } source && channels.ContainsKey(source))
                return source;

            return Processor is { } processor && attached.TryGetValue(processor, out var key) ? key : UnknownDevices;
        }

        private Channel ChannelFor(object key)
        {
            if (!channels.TryGetValue(key, out var channel))
                channels[key] = channel = new Channel();

            return channel;
        }

        public virtual void Draw(in Render3DContext render, DrawContext3D item)
            => ProcessorFor(render.SourceDevices)?.Draw(render, item);

        public virtual WorldBounds GetLocalBounds(in FrameContext itemTime)
            => Processor is I3DBounds provider ? provider.GetLocalBounds(itemTime) : WorldBounds.Empty;

        public virtual ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
            => Processor is I3DTextureProvider provider ? provider.GetTexture(device) : null;

        public virtual bool TryGetLocalMatrix(out System.Numerics.Matrix4x4 matrix)
        {
            if (Processor is I3DLocalTransform provider)
                return provider.TryGetLocalMatrix(out matrix);

            matrix = System.Numerics.Matrix4x4.Identity;
            return false;
        }

        public virtual bool TryGetSize(out System.Numerics.Vector2 size, out System.Numerics.Vector2 offset)
        {
            if (Processor is I3DSizeProvider provider)
                return provider.TryGetSize(out size, out offset);

            size = default;
            offset = default;
            return false;
        }

        public virtual bool ScalesToInputSize
            => Processor is not I3DSizeProvider provider || provider.ScalesToInputSize;
    }
}
