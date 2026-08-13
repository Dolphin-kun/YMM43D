using YukkuriMovieMaker.Commons;

namespace YMM43D.Scene3D
{
    public interface ICameraSync
    {
        void TouchCameraSync();
    }

    public sealed class CameraSync : ICameraSync
    {
        private const double Min = -1000000;
        private const double Max = 1000000;

        private int version;

        public Animation Value { get; } = new(0, Min, Max);

        public event Action? Changed;

        public void TouchCameraSync()
        {
            version = (version + 1) % (int)Max;
            Value.CopyFrom(new Animation(version, Min, Max));
            Changed?.Invoke();
        }
    }
}
