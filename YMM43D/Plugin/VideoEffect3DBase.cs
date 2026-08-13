using System.ComponentModel;

using Vortice.Direct3D11;

using YMM43D.Commons;

using YukkuriMovieMaker.Commons;

using YukkuriMovieMaker.Plugin.Effects;



namespace YMM43D.Plugin

{

    public abstract class VideoEffect3DBase

        : VideoEffectBase, I3DVideoEffect, ICameraSync, I3DSizeProvider, I3DLocalTransform, I3DBounds

    {

        private readonly CameraSync cameraSync = new();



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

            Processor = processor;

            return processor;

        }



        public void DetachProcessor(I3DProvider processor)

        {

            if (ReferenceEquals(Processor, processor))

                Processor = null;

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

