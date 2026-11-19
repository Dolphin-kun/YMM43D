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
        private const float PanStep = 0.03f;
        private const float DollyRatio = 0.2f;
        private const float FineRatio = 0.25f;
        private const float RollSpeed = 0.3f;
        private const float MinPivotDistance = 0.5f;
        private const float MaxPivotDistance = 200f;
        private const float FocusMargin = 2.5f;

        private enum DragMode { None, Rotate, Pan, Roll }

        private bool initialized;
        private bool pivotInitialized;
        private DragMode drag;
        private Point lastMousePosition;

        public CameraState State { get; private set; } = CameraState.Default;

        public void EnsureInitialized(in CameraState camera)
        {
            if (initialized)
                return;

            State = camera;
            initialized = true;

            if (pivotInitialized)
                return;

            PivotDistance = GuessPivotDistance(camera);
            pivotInitialized = true;
        }

        public void Reset()
        {
            initialized = false;
            pivotInitialized = false;
        }

        public void Invalidate() => initialized = false;

        public void AimAt(in Vector3 pivot, in CameraState basis)
        {
            var distance = Vector3.Dot(pivot - basis.Position, basis.Forward);

            if (!float.IsFinite(distance) || distance < MinPivotDistance)
                return;

            PivotDistance = MathF.Min(distance, MaxPivotDistance);
            pivotInitialized = true;
        }

        public CameraPose GetPose() => State.GetPose();

        public float PivotDistance { get; private set; } = SceneProjection.DefaultFocalDistance;

        public void Apply(in CameraMove move) => State = move.ApplyTo(State);

        public CameraMove? HandleMouse(Point position, D3D11Host.MouseEventKind kind, int delta, ModifierKeys modifiers, in CameraState basis)
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
                    return GetDragMove(difference, basis, Speed(modifiers));

                case D3D11Host.MouseEventKind.Wheel:
                    return Dolly(delta / 120f * Speed(modifiers), basis);

                default:
                    return null;
            }
        }

        public bool IsDragging => drag != DragMode.None;

        private static DragMode GetDragMode(ModifierKeys modifiers)
        {
            if ((modifiers & ModifierKeys.Shift) != 0)
                return DragMode.Pan;

            if ((modifiers & ModifierKeys.Alt) != 0)
                return DragMode.Roll;

            return DragMode.Rotate;
        }

        private static float Speed(ModifierKeys modifiers) => (modifiers & ModifierKeys.Control) != 0 ? FineRatio : 1f;

        private CameraMove? GetDragMove(System.Windows.Vector difference, in CameraState basis, float speed)
        {
            switch (drag)
            {
                case DragMode.Rotate:
                    return Orbit(-(float)difference.X * RotateSpeed * speed, -(float)difference.Y * RotateSpeed * speed, basis);

                case DragMode.Roll:
                    return new CameraMove(0f, 0f, -(float)difference.X * RollSpeed * speed, Vector3.Zero);

                case DragMode.Pan:
                    var rotation = basis.Rotation;
                    var right = Vector3.Transform(Vector3.UnitX, rotation);
                    var up = Vector3.Transform(Vector3.UnitY, rotation);
                    var step = PanStep * speed;

                    return CameraMove.Translate(right * (float)-difference.X * step + up * (float)difference.Y * step);

                default:
                    return null;
            }
        }

        private CameraMove Orbit(float yaw, float pitch, in CameraState basis)
        {
            var pivot = basis.Position + basis.Forward * PivotDistance;

            var turned = CameraMove.Rotate(yaw, pitch).ApplyTo(basis);
            var moved = pivot - turned.Forward * PivotDistance;

            return new CameraMove(yaw, pitch, 0f, moved - basis.Position);
        }

        private CameraMove Dolly(float notches, in CameraState basis)
        {
            var travel = PivotDistance * (1f - MathF.Pow(1f - DollyRatio, notches));

            if (!float.IsFinite(travel))
                return CameraMove.Translate(Vector3.Zero);

            PivotDistance = Math.Clamp(PivotDistance - travel, MinPivotDistance, MaxPivotDistance);

            return CameraMove.Translate(basis.Forward * travel);
        }

        public static CameraMove LevelRoll(in CameraState basis) => new(0f, 0f, -basis.Roll, Vector3.Zero);

        public CameraMove ViewFrom(float yaw, float pitch, in CameraState basis)
        {
            var pivot = basis.Position + basis.Forward * PivotDistance;
            var turned = basis with { Yaw = yaw, Pitch = pitch, Roll = 0f };

            return new CameraMove(yaw - basis.Yaw, pitch - basis.Pitch, -basis.Roll, pivot - turned.Forward * PivotDistance - basis.Position);
        }

        public CameraMove Focus(in WorldBounds bounds, in CameraState basis)
        {
            var center = bounds.Center;

            var radius = MathF.Max(Vector3.Distance(bounds.Min, bounds.Max) / 2f, 0.05f);

            var distance = Math.Clamp(radius * FocusMargin, MinPivotDistance, MaxPivotDistance);

            PivotDistance = distance;
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
