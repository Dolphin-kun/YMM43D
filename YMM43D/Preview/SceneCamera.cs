using System;
using System.Numerics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Preview
{
    /// <summary>
    /// シーン内の「カメラオブジェクト」の設定。
    /// </summary>
    [Serializable]
    public class SceneCamera : Bindable
    {
        public static SceneCamera Instance { get; } = new();

        private float cameraYaw = 0;
        private float cameraPitch = 0f;
        private float cameraRoll = 0f;
        private float cameraDistance = 10f;
        private Vector3 cameraTarget = Vector3.Zero;

        public float CameraYaw { get => cameraYaw; set => Set(ref cameraYaw, value, nameof(CameraYaw)); }
        public float CameraPitch { get => cameraPitch; set => Set(ref cameraPitch, value, nameof(CameraPitch)); }
        public float CameraRoll { get => cameraRoll; set => Set(ref cameraRoll, value, nameof(CameraRoll)); }
        public float CameraDistance { get => cameraDistance; set => Set(ref cameraDistance, value, nameof(CameraDistance)); }
        public Vector3 CameraTarget { get => cameraTarget; set => Set(ref cameraTarget, value, nameof(CameraTarget)); }

        public void Reset()
        {
            CameraYaw = 0;
            CameraPitch = 0f;
            CameraRoll = 0f;
            CameraDistance = 10f;
            CameraTarget = Vector3.Zero;
        }

        public Matrix4x4 GetViewMatrix()
        {
            var rotation = Matrix4x4.CreateRotationZ(CameraRoll) * Matrix4x4.CreateRotationX(CameraPitch) * Matrix4x4.CreateRotationY(CameraYaw);
            var lookDir = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            var cameraPos = CameraTarget - lookDir * CameraDistance;
            return Matrix4x4.CreateLookAt(cameraPos, CameraTarget, Vector3.Transform(Vector3.UnitY, rotation));
        }

        public Vector3 GetPosition()
        {
            var rotation = Matrix4x4.CreateRotationZ(CameraRoll) * Matrix4x4.CreateRotationX(CameraPitch) * Matrix4x4.CreateRotationY(CameraYaw);
            var lookDir = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            return CameraTarget - lookDir * CameraDistance;
        }

        public Matrix4x4 GetProjectionMatrix(float aspectRatio)
        {
            return Matrix4x4.CreatePerspectiveFieldOfView((float)Math.PI / 4, aspectRatio, 0.1f, 1000f);
        }
    }
}
