using System.Numerics;
using YMM43D.Scene3D;

namespace YMM43D.Integration
{
    /// <summary>
    /// カメラの値が前回の確認時から変化したかを判定します。
    /// </summary>
    /// <remarks>
    /// <see cref="SceneCamera"/> の値はキーフレームアニメーションなので、
    /// プロパティ変更通知だけでは「今のフレームでの見え方が変わったか」を判定できません。
    /// そのため実際に評価した値を覚えておいて比較します。
    /// </remarks>
    internal sealed class CameraChangeTracker
    {
        private const float Epsilon = 0.0001f;

        private bool hasSnapshot;
        private Snapshot last;

        /// <summary>
        /// 変化していれば <c>true</c> を返し、現在値を記録します。
        /// 初回の呼び出しは基準を作るだけなので <c>false</c> を返します。
        /// </summary>
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

        /// <summary>
        /// 変化判定を行わずに現在値だけを記録します。
        /// カメラを直接操作した直後に呼び、次回の判定で誤検知が起きないようにします。
        /// </summary>
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
