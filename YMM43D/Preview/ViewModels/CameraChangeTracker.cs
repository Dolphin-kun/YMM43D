using System;
using YMM43D.Preview;

namespace YMM43D.Preview.ViewModels
{
    internal readonly struct CameraSnapshot
    {
        public CameraSnapshot(float yaw, float pitch, float roll, float distance, float targetX, float targetY, float targetZ)
        {
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
            Distance = distance;
            TargetX = targetX;
            TargetY = targetY;
            TargetZ = targetZ;
        }

        public float Yaw { get; }
        public float Pitch { get; }
        public float Roll { get; }
        public float Distance { get; }
        public float TargetX { get; }
        public float TargetY { get; }
        public float TargetZ { get; }

        public static CameraSnapshot FromCamera(SceneCamera camera, int frame, int length, int fps)
        {
            var target = camera.CameraTarget;
            return new CameraSnapshot(
                (float)camera.CameraYaw.GetValue(frame, length, fps),
                (float)camera.CameraPitch.GetValue(frame, length, fps),
                (float)camera.CameraRoll.GetValue(frame, length, fps),
                (float)camera.CameraDistance.GetValue(frame, length, fps),
                target.X,
                target.Y,
                target.Z
            );
        }

        public bool NearlyEquals(CameraSnapshot other, float epsilon = 0.0001f)
        {
            return NearlyEqual(Yaw, other.Yaw, epsilon)
                && NearlyEqual(Pitch, other.Pitch, epsilon)
                && NearlyEqual(Roll, other.Roll, epsilon)
                && NearlyEqual(Distance, other.Distance, epsilon)
                && NearlyEqual(TargetX, other.TargetX, epsilon)
                && NearlyEqual(TargetY, other.TargetY, epsilon)
                && NearlyEqual(TargetZ, other.TargetZ, epsilon);
        }

        private static bool NearlyEqual(float a, float b, float epsilon)
        {
            return Math.Abs(a - b) < epsilon;
        }
    }

    internal sealed class CameraChangeTracker
    {
        private bool hasSnapshot;
        private CameraSnapshot lastSnapshot;

        public bool HasChanged(SceneCamera camera, int frame, int length, int fps)
        {
            var current = CameraSnapshot.FromCamera(camera, frame, length, fps);
            if (!hasSnapshot)
            {
                hasSnapshot = true;
                lastSnapshot = current;
                return false;
            }

            if (current.NearlyEquals(lastSnapshot))
                return false;

            lastSnapshot = current;
            return true;
        }

        public void Reset()
        {
            hasSnapshot = false;
        }
    }
}
