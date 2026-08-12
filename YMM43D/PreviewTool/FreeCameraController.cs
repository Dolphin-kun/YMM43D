using System.Numerics;
using System.Windows;
using YMM43D.Camera;
using YMM43D.PreviewTool.Views;

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
        private const float DollySpeed = 0.1f;

        /// <summary>視線の先を見失わないよう、軸までの距離に設ける範囲。</summary>
        private const float MinPivotDistance = 0.5f;
        private const float MaxPivotDistance = 200f;

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
        /// いま動かそうとしているカメラの設定値。回る軸と移動量の大きさをここから
        /// 決めるので、実際に動かす相手の値を渡してください。
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
                    return Dolly(delta / 120f, basis);

                default:
                    return null;
            }
        }

        /// <summary>ドラッグしている最中かどうか。</summary>
        public bool IsDragging => drag != DragMode.None;

        private CameraMove? GetDragMove(System.Windows.Vector difference, in CameraState basis)
        {
            switch (drag)
            {
                case DragMode.Rotate:
                    return Orbit(
                        -(float)difference.X * RotateSpeed,
                        -(float)difference.Y * RotateSpeed,
                        basis);

                case DragMode.Pan:
                    // 画面上の移動量を、視点の向きに合わせた平行移動に変換する。
                    // 見ている先までの距離に比例させることで、遠くから見ているときほど
                    // 大きく動き、寄っているときは細かく動かせる。
                    var rotation = basis.Rotation;
                    var right = Vector3.Transform(Vector3.UnitX, rotation);
                    var up = Vector3.Transform(Vector3.UnitY, rotation);
                    var scale = GetPivotDistance(basis) * PanSpeed;

                    return CameraMove.Translate(
                        right * (float)-difference.X * scale + up * (float)difference.Y * scale);

                default:
                    return null;
            }
        }

        /// <summary>
        /// 視線の先を軸にして回り込みます。
        /// </summary>
        /// <remarks>
        /// その場で首を振るだけだと、置いてあるものを別の角度から確かめられません。
        /// 向きを変えたぶんだけ位置もずらして、軸が画面の真ん中に留まるようにします。
        /// </remarks>
        private static CameraMove Orbit(float yaw, float pitch, in CameraState basis)
        {
            var distance = GetPivotDistance(basis);
            var pivot = basis.Position + basis.Forward * distance;

            var turned = CameraMove.Rotate(yaw, pitch).ApplyTo(basis);
            var moved = pivot - turned.Forward * distance;

            return new CameraMove(yaw, pitch, 0f, moved - basis.Position);
        }

        /// <summary>視線に沿って前後に動きます。</summary>
        private static CameraMove Dolly(float notches, in CameraState basis)
            => CameraMove.Translate(basis.Forward * (notches * GetPivotDistance(basis) * DollySpeed));

        /// <summary>
        /// 回る軸までの距離。視線が YMM4 の描く面（Z=0）と交わる所を軸にします。
        /// </summary>
        /// <remarks>
        /// アイテムはその面の近くに並ぶので、そこを軸にすると見ているものを中心に
        /// 回り込めます。面と交わらないほど水平に近い視線のときは、既定の距離に戻します。
        /// </remarks>
        private static float GetPivotDistance(in CameraState basis)
        {
            var forward = basis.Forward;

            if (MathF.Abs(forward.Z) < 0.05f)
                return SceneProjection.DefaultFocalDistance;

            var distance = -basis.Position.Z / forward.Z;

            return float.IsFinite(distance) && distance > MinPivotDistance
                ? MathF.Min(distance, MaxPivotDistance)
                : SceneProjection.DefaultFocalDistance;
        }
    }
}
