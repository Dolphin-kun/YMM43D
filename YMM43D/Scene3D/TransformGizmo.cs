using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>掴んだアイテムをどう動かすか。</summary>
    public enum GizmoHandle
    {
        /// <summary>何も掴んでいません。</summary>
        None,

        /// <summary>X 軸に沿って動かします。</summary>
        MoveX,

        /// <summary>Y 軸に沿って動かします。</summary>
        MoveY,

        /// <summary>Z 軸に沿って動かします。</summary>
        MoveZ,

        /// <summary>画面と平行に回します。</summary>
        RotateZ,

        /// <summary>軸を決めずに、画面に沿って動かします。</summary>
        Free,
    }

    /// <summary>
    /// アイテムを掴んで動かすための、軸と輪の当たり判定。
    /// </summary>
    /// <remarks>
    /// 何も案内が無いと、動かしたい向き以外にも動いてしまいます。矢印と輪を出して、
    /// それを掴んだときだけその軸に沿わせます。
    /// <para>
    /// 大きさはカメラからの距離に比例させます。寄っても引いても、画面上では同じ
    /// 大きさに見えます。
    /// </para>
    /// </remarks>
    /// <param name="Origin">アイテムの中心。</param>
    /// <param name="Scale">画面上で一定の大きさに見せるための倍率。</param>
    public readonly record struct TransformGizmo(Vector3 Origin, float Scale)
    {
        /// <summary>矢印の長さ（<see cref="Scale"/> を掛ける前）。</summary>
        public const float AxisLength = 1f;

        /// <summary>回す輪の半径（<see cref="Scale"/> を掛ける前）。</summary>
        public const float RingRadius = 0.75f;

        /// <summary>輪を何本の線で描くか。</summary>
        public const int RingSegments = 48;

        /// <summary>矢印の先にある羽の長さ。</summary>
        public const float HeadLength = 0.18f;

        /// <summary>カメラからの距離に対する大きさの割合。</summary>
        private const float ScreenRatio = 0.13f;

        /// <summary>掴んだと見なす、画面上の距離（ピクセル）。</summary>
        public const float GrabThreshold = 9f;

        /// <summary>アイテムの位置とカメラから、案内の大きさを決めます。</summary>
        public static TransformGizmo Create(in Vector3 origin, in Vector3 cameraPosition)
        {
            var distance = Vector3.Distance(origin, cameraPosition);

            return new TransformGizmo(origin, MathF.Max(0.05f, distance * ScreenRatio));
        }

        /// <summary>軸の向き。</summary>
        public static Vector3 AxisDirection(GizmoHandle handle) => handle switch
        {
            GizmoHandle.MoveX => Vector3.UnitX,
            GizmoHandle.MoveY => Vector3.UnitY,
            GizmoHandle.MoveZ => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        /// <summary>矢印の先端。</summary>
        public Vector3 AxisEnd(GizmoHandle handle)
            => Origin + AxisDirection(handle) * (AxisLength * Scale);

        /// <summary>輪の <paramref name="index"/> 番目の点。Z 軸のまわりを回ります。</summary>
        public Vector3 RingPoint(int index)
        {
            var angle = MathF.Tau * index / RingSegments;

            return Origin + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * (RingRadius * Scale);
        }

        /// <summary>
        /// 半直線と軸のうち、いちばん近づく所を軸上の位置として返します。
        /// </summary>
        /// <remarks>
        /// 軸に沿って動かすときに使います。マウスは画面上の1点しか指せないので、
        /// 軸に最も近い所を「そこを指した」と見なします。
        /// </remarks>
        public static float? ClosestOnAxis(in PickRay ray, in Vector3 origin, in Vector3 axis)
        {
            var w = origin - ray.Origin;

            var axisDotRay = Vector3.Dot(axis, ray.Direction);
            var determinant = 1f - axisDotRay * axisDotRay;

            // 軸と視線が平行。どこを指しても軸上の位置が決まらない。
            if (MathF.Abs(determinant) < 1e-6f)
                return null;

            var axisDotW = Vector3.Dot(axis, w);
            var rayDotW = Vector3.Dot(ray.Direction, w);

            return (axisDotRay * rayDotW - axisDotW) / determinant;
        }
    }
}
