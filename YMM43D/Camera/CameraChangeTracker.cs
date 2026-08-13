using YMM43D.Scene3D;

namespace YMM43D.Camera
{
    internal sealed class CameraChangeTracker
    {
        private bool hasSnapshot;
        private CameraState last;

        public bool HasChanged(in CameraState current)
        {
            if (!hasSnapshot)
            {
                hasSnapshot = true;
                last = current;
                return false;
            }

            if (current.NearlyEquals(last))
                return false;

            last = current;
            return true;
        }

        public void Sync(in CameraState current)
        {
            last = current;
            hasSnapshot = true;
        }
    }
}
