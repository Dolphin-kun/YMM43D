using System.Numerics;
using System.Windows;
using YMM43D.PreviewTool.Views;
using YMM43D.Scene3D;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// プレビュー上のマウス操作を、カメラをどう動かすかに翻訳します。
    /// </summary>
    /// <remarks>
    /// 返す差分は、プレビュー専用の視点にも、タイムラインのカメラアイテムにも
    /// そのまま使えます。ドラッグを絶対値ではなく差分として扱うのは、キーフレームを
    /// 打ったカメラを壊さずに動かせるようにするためです。
    /// </remarks>
    internal sealed class FreeCameraController
    {
        private const float RotateSpeed = 0.5f;
        private const float PanSpeed = 0.0015f;
        private const float ZoomSpeed = 0.005f;

        private enum DragMode { None, Rotate, Pan }

        private CameraState state = CameraState.Default;
        private bool initialized;
        private DragMode drag;
        private Point lastMousePosition;

        /// <summary>プレビュー専用の視点。</summary>
        public CameraState State => state;

        /// <summary>まだ同期していなければ、シーンのカメラに合わせます。</summary>
        public void EnsureInitialized(in CameraState camera)
        {
            if (initialized)
                return;

            state = camera;
            initialized = true;
        }

        /// <summary>次の機会にシーンのカメラへ合わせ直させます。</summary>
        public void Invalidate() => initialized = false;

        /// <summary>プレビュー専用の視点の姿勢。</summary>
        public CameraPose GetPose() => state.GetPose();

        /// <summary>差分をプレビュー専用の視点に効かせます。</summary>
        public void Apply(in CameraMove move) => state = move.ApplyTo(state);

        /// <summary>
        /// マウス操作を差分に翻訳します。動かす量が無ければ <c>null</c> を返します。
        /// </summary>
        /// <param name="basis">
        /// いま動かそうとしているカメラの設定値。平行移動の向きと大きさを決めるのに
        /// 使います。プレビュー専用の視点を動かすならその値、カメラアイテムを動かす
        /// ならそちらの値を渡してください。
        /// </param>
        public CameraMove? HandleMouse(
            Point position, D3D11Host.MouseEventKind kind, int delta, in CameraState basis)
        {
            switch (kind)
            {
                case D3D11Host.MouseEventKind.Down:
                    lastMousePosition = position;
                    drag = DragMode.Rotate;
                    return null;

                case D3D11Host.MouseEventKind.RightDown:
                    lastMousePosition = position;
                    drag = DragMode.Pan;
                    return null;

                case D3D11Host.MouseEventKind.Up:
                case D3D11Host.MouseEventKind.RightUp:
                    drag = DragMode.None;
                    return null;

                case D3D11Host.MouseEventKind.Move:
                    var difference = position - lastMousePosition;
                    lastMousePosition = position;
                    return GetDragMove(difference, basis);

                case D3D11Host.MouseEventKind.Wheel:
                    return new CameraMove(0f, 0f, 0f, -delta * ZoomSpeed, Vector3.Zero);

                default:
                    return null;
            }
        }

        /// <summary>ドラッグしている最中かどうか。</summary>
        public bool IsDragging => drag != DragMode.None;

        private CameraMove? GetDragMove(System.Windows.Vector difference, CameraState basis)
        {
            switch (drag)
            {
                case DragMode.Rotate:
                    return new CameraMove(
                        -(float)difference.X * RotateSpeed,
                        -(float)difference.Y * RotateSpeed,
                        0f, 0f, Vector3.Zero);

                case DragMode.Pan:
                    // 画面上の移動量を、視点の向きに合わせた平行移動に変換する。
                    // 距離に比例させることで、遠くから見ているときほど大きく動く。
                    var rotation = Rotation3D.ForCamera(basis.Yaw, basis.Pitch, basis.Roll);
                    var right = Vector3.Transform(Vector3.UnitX, rotation);
                    var up = Vector3.Transform(Vector3.UnitY, rotation);

                    var move = right * (float)-difference.X * basis.Distance * PanSpeed
                             + up * (float)difference.Y * basis.Distance * PanSpeed;

                    return new CameraMove(0f, 0f, 0f, 0f, move);

                default:
                    return null;
            }
        }
    }
}
