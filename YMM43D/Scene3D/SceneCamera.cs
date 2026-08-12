using System.Numerics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// シーンを撮影するカメラ。3Dプレビューと動画出力の両方で、
    /// このカメラのビュー行列を使って描画されます。
    /// </summary>
    /// <remarks>
    /// 注視点を中心に、指定した距離だけ離れた位置から見る軌道カメラです。
    /// 角度と距離は <see cref="Animation"/> なのでキーフレームを打てます。
    /// </remarks>
    public sealed class SceneCamera : Bindable
    {
        /// <summary>画面の大きさが分からない場合に使う垂直画角（ラジアン）。</summary>
        public const float DefaultFieldOfView = MathF.PI / 4f;

        private const float NearPlane = 0.1f;
        private const float FarPlane = 1000f;

        private const float MinDistance = 0.1f;

        private Vector3 target = Vector3.Zero;
        private bool isControlledByPreviewDrag;

        /// <summary>水平方向の回転角（度）。</summary>
        public Animation Yaw { get; } = new(0, -3600, 3600);

        /// <summary>垂直方向の回転角（度）。</summary>
        public Animation Pitch { get; } = new(0, -90, 90);

        /// <summary>視線周りの回転角（度）。</summary>
        public Animation Roll { get; } = new(0, -3600, 3600);

        /// <summary>注視点からカメラまでの距離。</summary>
        public Animation Distance { get; } = new(10, 0.1, 1000);

        /// <summary>カメラが向く先の座標。</summary>
        public Vector3 Target
        {
            get => target;
            set => Set(ref target, value, nameof(Target));
        }

        /// <summary>
        /// <c>true</c> のとき、3Dプレビュー上のドラッグ操作がこのカメラを直接動かします。
        /// <c>false</c> のときはプレビュー専用の視点だけが動き、出力には影響しません。
        /// </summary>
        public bool IsControlledByPreviewDrag
        {
            get => isControlledByPreviewDrag;
            set => Set(ref isControlledByPreviewDrag, value, nameof(IsControlledByPreviewDrag));
        }

        /// <summary>すべての値を初期状態に戻します。</summary>
        public void Reset()
        {
            Yaw.CopyFrom(new Animation(0, -3600, 3600));
            Pitch.CopyFrom(new Animation(0, -90, 90));
            Roll.CopyFrom(new Animation(0, -3600, 3600));
            Distance.CopyFrom(new Animation(10, 0.1, 1000));
            Target = Vector3.Zero;
        }

        /// <summary>別のカメラの状態をこのカメラに写します。</summary>
        public void CopyFrom(SceneCamera other)
        {
            Yaw.CopyFrom(other.Yaw);
            Pitch.CopyFrom(other.Pitch);
            Roll.CopyFrom(other.Roll);
            Distance.CopyFrom(other.Distance);
            Target = other.Target;
        }

        /// <summary>
        /// 指定時点の姿勢を、角度・距離・注視点から解決します。
        /// </summary>
        public CameraPose GetPose(in FrameContext time)
        {
            var rotation = Rotation3D.ForCamera(
                Yaw.GetFloat(time),
                Pitch.GetFloat(time),
                Roll.GetFloat(time));

            var forward = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            var up = Vector3.Transform(Vector3.UnitY, rotation);
            var position = Target - forward * Distance.GetFloat(time);

            return new CameraPose(position, Target, up, rotation);
        }

        /// <summary>指定時点のビュー行列。</summary>
        public Matrix4x4 GetViewMatrix(in FrameContext time) => GetPose(time).ViewMatrix;
        /// <summary>
        /// 射影行列。クリップ面はシーン全体で共通のため静的メソッドです。
        /// </summary>
        /// <param name="aspectRatio">描画先の横縦比。</param>
        /// <param name="screenHeight">動画の画面の高さ（ピクセル）。</param>
        /// <param name="distance">注視点までの距離。</param>
        public static Matrix4x4 GetProjectionMatrix(float aspectRatio, float screenHeight, float distance)
            => Matrix4x4.CreatePerspectiveFieldOfView(
                GetFieldOfView(screenHeight, distance), aspectRatio, NearPlane, FarPlane);

        /// <summary>
        /// 視線からの傾き（<c>x / -z</c>）をそのまま出す射影行列を返します。
        /// </summary>
        /// <remarks>
        /// 除算後の x・y が傾きそのものになります。ここから先は <see cref="ImageProjection"/> が
        /// 2D のアフィン変換として画面ピクセル・アイテムの画像・NDC へ順に移します。
        /// クリップ面はここで決まるので、どのアイテムを描くときも深度の目盛りは共通です。
        /// </remarks>
        public static Matrix4x4 GetTangentProjection()
            => Matrix4x4.CreatePerspectiveOffCenter(
                -NearPlane, NearPlane, -NearPlane, NearPlane, NearPlane, FarPlane);

        /// <summary>
        /// 視線からの傾き（正接）1 あたりのピクセル数を求めます。
        /// </summary>
        /// <remarks>
        /// 注視点の面（Z=0）にあるワールド1単位は、傾きにすると <c>1/距離</c> です。
        /// これに掛けた結果が <see cref="WorldScale.PixelsPerUnit"/> になるよう定めて
        /// いるので、Z=0 のアイテムは YMM4 が 2D で描く大きさと一致します。
        /// </remarks>
        public static float GetPixelsPerTangent(float distance)
            => WorldScale.PixelsPerUnit * MathF.Max(distance, MinDistance);

        /// <summary>
        /// 垂直画角（ラジアン）を求めます。
        /// </summary>
        /// <remarks>
        /// 画角は固定値ではなく、注視点の面が画面とちょうど1対1で対応するように決めます。
        /// 固定の 45°・距離 10 では画面の高さ 1080px に対して 828px 分しか写らず、
        /// 1.30 倍に引き伸ばされていました。
        /// <para>
        /// この決め方だと、<see cref="Distance"/> は「寄り引き」ではなく「遠近の強さ」を
        /// 決めるつまみになります。近づけるほど画角が広がり、手前と奥の差が強く出ます。
        /// </para>
        /// </remarks>
        public static float GetFieldOfView(float screenHeight, float distance)
        {
            // 画面の大きさが取れない場面では既定値に戻す。
            if (!float.IsFinite(screenHeight) || screenHeight <= 0)
                return DefaultFieldOfView;

            return 2f * MathF.Atan(screenHeight / (2f * GetPixelsPerTangent(distance)));
        }
    }

    /// <summary>
    /// ある時点におけるカメラの姿勢。
    /// </summary>
    public readonly record struct CameraPose(Vector3 Position, Vector3 Target, Vector3 Up, Matrix4x4 Rotation)
    {
        /// <summary>この姿勢に対応するビュー行列。</summary>
        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Up);

        /// <summary>カメラそのものを 3D 空間に描くためのワールド行列。</summary>
        public Matrix4x4 WorldMatrix => Rotation * Matrix4x4.CreateTranslation(Position);
    }
}
