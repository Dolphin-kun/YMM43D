using System.Numerics;
using System.Windows;
using YMM43D.Scene3D;
using YMM43D.PreviewTool.Views;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool
{
    internal sealed class FreeCameraController
    {
        private const float RotateSpeed = 0.5f;
        private const float PanSpeed = 0.0015f;
        private const float ZoomSpeed = 0.005f;
        private const float MinDistance = 0.1f;
        private const float MaxPitch = 89.9f;

        private enum DragMode { None, Rotate, Pan }

        private float yaw;
        private float pitch;
        private float roll;
        private float distance = 10f;
        private Vector3 target = Vector3.Zero;

        private bool initialized;
        private DragMode drag;
        private Point lastMousePosition;

        public void EnsureInitialized(in CameraState camera)
        {
            if (initialized)
                return;

            yaw = camera.Yaw;
            pitch = camera.Pitch;
            roll = camera.Roll;
            distance = camera.Distance;
            target = camera.Target;
            initialized = true;
        }

        public void Invalidate() => initialized = false;

        public CameraPose GetPose() => new CameraState(yaw, pitch, roll, distance, target).GetPose();

        public void ApplyTo(SceneCamera camera)
        {
            if (!initialized)
                return;

            camera.Yaw.CopyFrom(new Animation(yaw, -3600, 3600));
            camera.Pitch.CopyFrom(new Animation(pitch, -90, 90));
            camera.Roll.CopyFrom(new Animation(roll, -3600, 3600));
            camera.Distance.CopyFrom(new Animation(distance, MinDistance, 1000));
            camera.Target = target;
        }

        public bool HandleMouse(Point position, D3D11Host.MouseEventKind kind, int delta)
        {
            switch (kind)
            {
                case D3D11Host.MouseEventKind.Down:
                    lastMousePosition = position;
                    drag = DragMode.Rotate;
                    return false;

                case D3D11Host.MouseEventKind.RightDown:
                    lastMousePosition = position;
                    drag = DragMode.Pan;
                    return false;

                case D3D11Host.MouseEventKind.Up:
                case D3D11Host.MouseEventKind.RightUp:
                    drag = DragMode.None;
                    return true;

                case D3D11Host.MouseEventKind.Move:
                    var difference = position - lastMousePosition;
                    lastMousePosition = position;
                    return ApplyDrag(difference);

                case D3D11Host.MouseEventKind.Wheel:
                    distance = Math.Max(MinDistance, distance - delta * ZoomSpeed);
                    return true;

                default:
                    return false;
            }
        }

        private bool ApplyDrag(System.Windows.Vector difference)
        {
            switch (drag)
            {
                case DragMode.Rotate:
                    yaw -= (float)difference.X * RotateSpeed;
                    pitch = Math.Clamp(pitch - (float)difference.Y * RotateSpeed, -MaxPitch, MaxPitch);
                    return true;

                case DragMode.Pan:
                    // 画面上の移動量を、視点の向きに合わせた平行移動に変換する。
                    // 距離に比例させることで、遠くから見ているときほど大きく動く。
                    var rotation = Rotation3D.ForCamera(yaw, pitch, roll);
                    var right = Vector3.Transform(Vector3.UnitX, rotation);
                    var up = Vector3.Transform(Vector3.UnitY, rotation);
                    target += right * (float)-difference.X * distance * PanSpeed;
                    target += up * (float)difference.Y * distance * PanSpeed;
                    return true;

                default:
                    return false;
            }
        }
    }
}
