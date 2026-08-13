using System.Numerics;
using System.Windows;
using System.Windows.Input;
using YMM43D.Camera;
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

        /// <summary>傾ける速さ。回すより控えめにしないと、少し動かしただけで大きく傾く。</summary>
        private const float RollSpeed = 0.3f;

        /// <summary>ホイール1目盛りで、軸までの距離が何倍になるか。</summary>
        private const float DollyRatio = 0.9f;

        /// <summary>視線の先を見失わないよう、軸までの距離に設ける範囲。</summary>
        private const float MinPivotDistance = 0.5f;
        private const float MaxPivotDistance = 200f;

        /// <summary>注視したものが画面に収まるよう、範囲の半径の何倍まで離れるか。</summary>
        private const float FocusMargin = 2.5f;

        private enum DragMode { None, Rotate, Pan, Roll }

        private CameraState state = CameraState.Default;

        // 回る軸までの距離。回転・平行移動・寄り引きの効き具合がこれで決まる。
        // 毎回カメラの位置から計算し直すと、向きを変えただけで効き具合が変わって
        // しまうので、状態として持ってホイールでだけ変える。
        private float pivotDistance = SceneProjection.DefaultFocalDistance;

        private bool initialized;
        private bool pivotInitialized;
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

            // 軸までの距離はホイールで決めた値を保ちたい。カメラに追従している間は
            // 毎フレームここを通るので、ここで測り直すと効き具合が定まらない。
            if (pivotInitialized)
                return;

            pivotDistance = GuessPivotDistance(camera);
            pivotInitialized = true;
        }

        /// <summary>回る軸までの距離。</summary>
        public float PivotDistance => pivotDistance;

        /// <summary>視点も、回る軸までの距離も、シーンのカメラに合わせ直させます。</summary>
        public void Reset()
        {
            initialized = false;
            pivotInitialized = false;
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
        /// <param name="modifiers">押されている修飾キー。ドラッグの意味がこれで変わります。</param>
        /// <param name="basis">
        /// いま動かそうとしているカメラの設定値。回る軸と移動量の大きさをここから
        /// 決めるので、実際に動かす相手の値を渡してください。
        /// </param>
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

        /// <summary>ドラッグしている最中かどうか。</summary>
        public bool IsDragging => drag != DragMode.None;

        /// <summary>
        /// 修飾キーからドラッグの意味を決めます。
        /// </summary>
        /// <remarks>
        /// Blender と同じ割り当てです。Shift で平行移動、Ctrl で傾き、何も押さなければ
        /// 回り込みになります。
        /// </remarks>
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
                    // 視線そのものは変わらないので、回る軸も見ている先も動かない。
                    // 回転の順が「傾き→垂直→水平」なので、傾きは前方向に影響しない。
                    return new CameraMove(0f, 0f, -(float)difference.X * RollSpeed, Vector3.Zero);

                case DragMode.Pan:
                    // 画面上の移動量を、視点の向きに合わせた平行移動に変換する。
                    // 軸までの距離に比例させることで、遠くから見ているときほど大きく
                    // 動き、寄っているときは細かく動かせる。
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

        /// <summary>
        /// 視線の先を軸にして回り込みます。
        /// </summary>
        /// <remarks>
        /// その場で首を振るだけだと、置いてあるものを別の角度から確かめられません。
        /// 向きを変えたぶんだけ位置もずらして、軸が画面の真ん中に留まるようにします。
        /// </remarks>
        private CameraMove Orbit(float yaw, float pitch, in CameraState basis)
        {
            var pivot = basis.Position + basis.Forward * pivotDistance;

            var turned = CameraMove.Rotate(yaw, pitch).ApplyTo(basis);
            var moved = pivot - turned.Forward * pivotDistance;

            return new CameraMove(yaw, pitch, 0f, moved - basis.Position);
        }

        /// <summary>
        /// 視線に沿って、軸に近づいたり離れたりします。
        /// </summary>
        /// <remarks>
        /// 1目盛りで距離が一定の割合だけ縮みます。近づくほど動く量も小さくなるので、
        /// どこまで寄っても行き過ぎません。軸そのものは動かないので、寄り引きしても
        /// 見ている対象を見失いません。
        /// </remarks>
        private CameraMove Dolly(float notches, in CameraState basis)
        {
            var next = Math.Clamp(
                pivotDistance * MathF.Pow(DollyRatio, notches), MinPivotDistance, MaxPivotDistance);

            var shift = basis.Forward * (pivotDistance - next);
            pivotDistance = next;

            return CameraMove.Translate(shift);
        }

        /// <summary>
        /// 傾きを 0 に戻す差分。
        /// </summary>
        /// <remarks>
        /// 傾いたままだと、回り込みも平行移動も斜めに効くので直しようがなくなります。
        /// 水平に戻す手段だけは、ドラッグとは別に用意しておきます。
        /// </remarks>
        public static CameraMove LevelRoll(in CameraState basis)
            => new(0f, 0f, -basis.Roll, Vector3.Zero);

        /// <summary>
        /// 決まった向きから見る差分。
        /// </summary>
        /// <remarks>
        /// 見ている先は変えずに、そのまわりを回ってその向きに付けます。真上・真下は
        /// 視線が定まらなくなる手前で止めます。
        /// </remarks>
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

        /// <summary>
        /// 与えた範囲が画面に収まる所まで寄る差分。
        /// </summary>
        /// <remarks>
        /// 向きは変えずに、範囲の中心が回る軸になるよう位置だけを動かします。以後の
        /// 回り込みや平行移動も、その範囲を中心に効くようになります。
        /// </remarks>
        public CameraMove Focus(in WorldBounds bounds, in CameraState basis)
        {
            var center = (bounds.Min + bounds.Max) / 2f;

            // 板のように潰れた形でも寄りすぎないよう、下限を設ける。
            var radius = MathF.Max(Vector3.Distance(bounds.Min, bounds.Max) / 2f, 0.05f);

            var distance = Math.Clamp(radius * FocusMargin, MinPivotDistance, MaxPivotDistance);

            pivotDistance = distance;
            pivotInitialized = true;

            return CameraMove.Translate(center - basis.Forward * distance - basis.Position);
        }

        /// <summary>
        /// 別のカメラに合わせたときの、軸までの距離の初期値。
        /// </summary>
        /// <remarks>
        /// 視線が YMM4 の描く面（Z=0）と交わる所を軸にします。アイテムはその面の
        /// 近くに並ぶので、見ているものを中心に回り込めます。面と交わらないほど
        /// 水平に近い視線のときは、既定の距離に戻します。
        /// </remarks>
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
