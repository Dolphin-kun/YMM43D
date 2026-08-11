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
        /// <summary>既定の垂直画角（ラジアン）。</summary>
        public const float DefaultFieldOfView = MathF.PI / 4f;

        private const float NearPlane = 0.1f;
        private const float FarPlane = 1000f;

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

        /// <summary>指定時点のカメラ位置。</summary>
        public Vector3 GetPosition(in FrameContext time) => GetPose(time).Position;

        /// <summary>
        /// 射影行列。画角・クリップ面はシーン全体で共通のため静的メソッドです。
        /// </summary>
        public static Matrix4x4 GetProjectionMatrix(float aspectRatio)
            => GetProjectionMatrix(aspectRatio, DefaultFieldOfView);

        /// <summary>
        /// 画角を指定して射影行列を作ります。
        /// </summary>
        /// <remarks>
        /// 画面全体より小さな描画先に、同じ縮尺で描きたい場合に使います。
        /// 描画先が小さいぶん画角を狭めれば、1単位あたりのピクセル数を保てます。
        /// </remarks>
        public static Matrix4x4 GetProjectionMatrix(float aspectRatio, float verticalFieldOfView)
            => Matrix4x4.CreatePerspectiveFieldOfView(verticalFieldOfView, aspectRatio, NearPlane, FarPlane);

        /// <summary>
        /// 視線の正面から外れた範囲を写す射影行列を作ります。
        /// </summary>
        /// <param name="minTangent">範囲の左下を、視線からの傾き（<c>x / -z</c>）で表したもの。</param>
        /// <param name="maxTangent">範囲の右上を、同じく傾きで表したもの。</param>
        /// <remarks>
        /// 画面の一部だけを、元の縮尺のまま切り出して描くために使います。中心を
        /// ずらせるので、対象が視線の正面から外れていても無駄なく収められます。
        /// </remarks>
        public static Matrix4x4 GetProjectionMatrix(Vector2 minTangent, Vector2 maxTangent)
            => Matrix4x4.CreatePerspectiveOffCenter(
                minTangent.X * NearPlane,
                maxTangent.X * NearPlane,
                minTangent.Y * NearPlane,
                maxTangent.Y * NearPlane,
                NearPlane,
                FarPlane);

        /// <summary>
        /// 画面全体に描いたときの、ワールド1単位あたりのピクセル数を求めます。
        /// </summary>
        /// <param name="distance">カメラから対象までの距離。</param>
        /// <param name="screenHeight">画面の高さ（ピクセル）。</param>
        public static float GetPixelsPerUnit(float distance, float screenHeight)
        {
            if (distance <= 0)
                return 0;

            return GetPixelsPerTangent(screenHeight) / distance;
        }

        /// <summary>
        /// 視線からの傾き（正接）1 あたりのピクセル数を求めます。
        /// </summary>
        /// <remarks>
        /// カメラから見た方向を <c>x / -z</c> の形で表したとき、それに掛ければ
        /// 画面上のピクセル位置になります。距離で割る前の <see cref="GetPixelsPerUnit"/>
        /// にあたる値で、遠近を含めて位置を求めるときに使います。
        /// </remarks>
        public static float GetPixelsPerTangent(float screenHeight)
            => screenHeight / (2f * MathF.Tan(DefaultFieldOfView / 2f));

        /// <summary>
        /// 画面全体より小さな描画先で、<see cref="GetPixelsPerUnit"/> と同じ縮尺を
        /// 保つための画角を求めます。
        /// </summary>
        public static float GetFieldOfViewFor(float targetHeight, float screenHeight)
        {
            // 画角 0 は射影行列を作れない。描画先の大きさが取れない場合は既定値に戻す。
            if (targetHeight <= 0 || screenHeight <= 0)
                return DefaultFieldOfView;

            return 2f * MathF.Atan(MathF.Tan(DefaultFieldOfView / 2f) * targetHeight / screenHeight);
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
