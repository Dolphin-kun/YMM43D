using System.Numerics;
using YMM43D.Scene3D;

namespace YMM43D.Integration
{
    internal sealed class CameraChangeTracker
    {
        private const float Epsilon = 0.0001f;

        private bool hasSnapshot;
        private Snapshot last;

        public bool HasChanged(SceneCamera camera, in FrameContext time)
        {
            var current = Snapshot.Capture(camera, time);

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
        public void Sync(SceneCamera camera, in FrameContext time)
        {
            last = Snapshot.Capture(camera, time);
            hasSnapshot = true;
        }

        private readonly record struct Snapshot(float Yaw, float Pitch, float Roll, float Distance, Vector3 Target)
        {
            public static Snapshot Capture(SceneCamera camera, in FrameContext time) => new(
                camera.Yaw.GetFloat(time),
                camera.Pitch.GetFloat(time),
                camera.Roll.GetFloat(time),
                camera.Distance.GetFloat(time),
                camera.Target);

            public bool NearlyEquals(in Snapshot other)
                => MathF.Abs(Yaw - other.Yaw) < Epsilon
                && MathF.Abs(Pitch - other.Pitch) < Epsilon
                && MathF.Abs(Roll - other.Roll) < Epsilon
                && MathF.Abs(Distance - other.Distance) < Epsilon
                && Vector3.DistanceSquared(Target, other.Target) < Epsilon * Epsilon;
        }
    }
}
