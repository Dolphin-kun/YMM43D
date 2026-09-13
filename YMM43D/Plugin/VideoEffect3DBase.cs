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
        private readonly CameraSync cameraSync = new();
        private readonly Lock instanceGate = new();
        private readonly HashSet<I3DProvider> attached = new(ReferenceEqualityComparer.Instance);
        private I3DProvider?[] inputs = [];

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
                attached.Add(processor);

            return processor;
        }

        public void DetachProcessor(I3DProvider processor)
        {
            if (ReferenceEquals(Processor, processor))
                Processor = null;

            lock (instanceGate)
            {
                attached.Remove(processor);

                for (var i = 0; i < inputs.Length; i++)
                {
                    if (ReferenceEquals(inputs[i], processor))
                        inputs[i] = null;
                }
            }
        }

        internal void ReportInput(I3DProvider processor, int index, int count)
        {
            lock (instanceGate)
            {
                if (!attached.Contains(processor) || index < 0 || index >= count)
                    return;

                if (inputs.Length != count)
                    inputs = new I3DProvider?[count];

                inputs[index] = processor;
            }
        }

        public IReadOnlyList<I3DProvider> GetInstances()
        {
            lock (instanceGate)
            {
                if (inputs.Length < 2)
                    return [this];

                I3DProvider[] found = [.. inputs.OfType<I3DProvider>()];

                return found.Length > 0 ? found : [this];
            }
        }

        public virtual void Draw(in Render3DContext render, DrawContext3D item)
            => Processor?.Draw(render, item);

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
