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

        // ここでの記録は「今の値を基準に置き直す」という意味。カメラを直接
        // 動かした直後に呼ばないと、次の HasChanged が同じ変化を拾い直す。
        public void Sync(in CameraState current)
        {
            last = current;
            hasSnapshot = true;
        }
    }
}
