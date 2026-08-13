
namespace YMM43D.Commons
{
    internal sealed class CameraChangeTracker
    {
        private bool hasSnapshot;
        private CameraState lastCamera;
        private SceneLighting lastLighting = SceneLighting.Default;

        public bool HasChanged(in CameraState camera, SceneLighting lighting)
        {
            if (!hasSnapshot)
            {
                Sync(camera, lighting);
                return false;
            }

            if (camera.NearlyEquals(lastCamera) && lighting.NearlyEquals(lastLighting))
                return false;

            Sync(camera, lighting);
            return true;
        }

        public void Sync(in CameraState camera, SceneLighting lighting)
        {
            lastCamera = camera;
            lastLighting = lighting;
            hasSnapshot = true;
        }
    }
}
