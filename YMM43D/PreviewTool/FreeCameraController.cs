using System.Numerics;
using System.Windows;
using System.Windows.Input;
using YMM43D.Commons;
using YMM43D.PreviewTool.Views;

namespace YMM43D.PreviewTool
{
    internal sealed class FreeCameraController
    {
        private const float RotateSpeed = 0.5f;
        private const float PanSpeed = 0.0015f;

        private const float RollSpeed = 0.3f;

        private const float DollyRatio = 0.9f;

        private const float MinPivotDistance = 0.5f;
        private const float MaxPivotDistance = 200f;

        private const float FocusMargin = 2.5f;

        private enum DragMode { None, Rotate, Pan, Roll }

        private CameraState state = CameraState.Default;

        private float pivotDistance = SceneProjection.DefaultFocalDistance;

        private bool initialized;
        private bool pivotInitialized;
        private DragMode drag;
        private Point lastMousePosition;

        public CameraState State => state;

        public void EnsureInitialized(in CameraState camera)
        {
            if (initialized)
                return;

            state = camera;
            initialized = true;

            if (pivotInitialized)
                return;

            pivotDistance = GuessPivotDistance(camera);
            pivotInitialized = true;
        }

        public float PivotDistance => pivotDistance;

        public void Reset()
        {
            initialized = false;
            pivotInitialized = false;
        }

        public void Invalidate() => initialized = false;

        public CameraPose GetPose() => state.GetPose();

        public void Apply(in CameraMove move) => state = move.ApplyTo(state);

        public CameraMove? HandleMouse(
            Point position,
            D3D11Host.MouseEventKind kind,
            int delta,
            ModifierKeys modifiers,
            in CameraState basis)
        {
            switch (kind)
            {
                case D3D11Host.MouseEventKind.Down:
                case D3D11Host.MouseEventKind.MiddleDown:
                    lastMousePosition = position;
                    drag = GetDragMode(modifiers);
                    return null;

                case D3D11Host.MouseEventKind.RightDown:
                    lastMousePosition = position;
                    drag = DragMode.Pan;
                    return null;

                case D3D11Host.MouseEventKind.Up:
                case D3D11Host.MouseEventKind.RightUp:
                case D3D11Host.MouseEventKind.MiddleUp:
                    drag = DragMode.None;
                    return null;

                case D3D11Host.MouseEventKind.Move:
                    var difference = position - lastMousePosition;
                    lastMousePosition = position;
                    return GetDragMove(difference, basis);

                case D3D11Host.MouseEventKind.Wheel:
                    return Dolly(delta / 120f, basis);

                default:
                    return null;
            }
        }

        public bool IsDragging => drag != DragMode.None;

        private static DragMode GetDragMode(ModifierKeys modifiers)
        {
            if ((modifiers & ModifierKeys.Shift) != 0)
                return DragMode.Pan;

            if ((modifiers & ModifierKeys.Control) != 0)
                return DragMode.Roll;

            return DragMode.Rotate;
        }

        private CameraMove? GetDragMove(System.Windows.Vector difference, in CameraState basis)
        {
            switch (drag)
            {
                case DragMode.Rotate:
                    return Orbit(
                        -(float)difference.X * RotateSpeed,
                        -(float)difference.Y * RotateSpeed,
                        basis);

                case DragMode.Roll:
                    return new CameraMove(0f, 0f, -(float)difference.X * RollSpeed, Vector3.Zero);

                case DragMode.Pan:
                    var rotation = basis.Rotation;
                    var right = Vector3.Transform(Vector3.UnitX, rotation);
                    var up = Vector3.Transform(Vector3.UnitY, rotation);
                    var scale = pivotDistance * PanSpeed;

                    return CameraMove.Translate(
                        right * (float)-difference.X * scale + up * (float)difference.Y * scale);

                default:
                    return null;
            }
        }

        private CameraMove Orbit(float yaw, float pitch, in CameraState basis)
        {
            var pivot = basis.Position + basis.Forward * pivotDistance;

            var turned = CameraMove.Rotate(yaw, pitch).ApplyTo(basis);
            var moved = pivot - turned.Forward * pivotDistance;

            return new CameraMove(yaw, pitch, 0f, moved - basis.Position);
        }

        private CameraMove Dolly(float notches, in CameraState basis)
        {
            var next = Math.Clamp(
                pivotDistance * MathF.Pow(DollyRatio, notches), MinPivotDistance, MaxPivotDistance);

            var shift = basis.Forward * (pivotDistance - next);
            pivotDistance = next;

            return CameraMove.Translate(shift);
        }

        public static CameraMove LevelRoll(in CameraState basis)
            => new(0f, 0f, -basis.Roll, Vector3.Zero);

        public CameraMove ViewFrom(float yaw, float pitch, in CameraState basis)
        {
            var pivot = basis.Position + basis.Forward * pivotDistance;
            var turned = basis with { Yaw = yaw, Pitch = pitch, Roll = 0f };

            return new CameraMove(
                yaw - basis.Yaw,
                pitch - basis.Pitch,
                -basis.Roll,
                pivot - turned.Forward * pivotDistance - basis.Position);
        }

        public CameraMove Focus(in WorldBounds bounds, in CameraState basis)
        {
            var center = (bounds.Min + bounds.Max) / 2f;

            var radius = MathF.Max(Vector3.Distance(bounds.Min, bounds.Max) / 2f, 0.05f);

            var distance = Math.Clamp(radius * FocusMargin, MinPivotDistance, MaxPivotDistance);

            pivotDistance = distance;
            pivotInitialized = true;

            return CameraMove.Translate(center - basis.Forward * distance - basis.Position);
        }

        private static float GuessPivotDistance(in CameraState camera)
        {
            var forward = camera.Forward;

            if (MathF.Abs(forward.Z) < 0.05f)
                return SceneProjection.DefaultFocalDistance;

            var distance = -camera.Position.Z / forward.Z;

            return float.IsFinite(distance) && distance > MinPivotDistance
                ? MathF.Min(distance, MaxPivotDistance)
                : SceneProjection.DefaultFocalDistance;
        }
    }
}
