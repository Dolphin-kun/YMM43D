using System.ComponentModel;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM43D.Plugin
{
    public abstract class ShapeParameter3DBase : ShapeParameterBase, ICameraSync
    {
        private readonly CameraSync cameraSync = new();

        protected ShapeParameter3DBase(SharedDataStore? sharedData) : base(sharedData)
        {
            cameraSync.Changed += () => OnPropertyChanged(nameof(CameraSyncAnimation));
        }

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public Animation CameraSyncAnimation => cameraSync.Value;

        public void TouchCameraSync() => cameraSync.TouchCameraSync();

        protected abstract Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices);

        public sealed override IShapeSource CreateShapeSource(IGraphicsDevicesAndContext devices)
        {
            var source = Create3DSource(devices);
            Provider3DRegistry.Register(this, source);
            return source;
        }
    }
}
