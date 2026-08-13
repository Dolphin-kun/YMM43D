using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// 画面上の1点から、ワールド空間へ伸ばした半直線。
    /// </summary>
    /// <remarks>
    /// 3Dプレビューでアイテムを掴むために使います。画面の座標だけでは奥行きが
    /// 決まらないので、視点から伸ばした線と物の交わりで「何を指したか」を決めます。
    /// </remarks>
    /// <param name="Origin">
    /// 始点。<see cref="FromScreen"/> で作ると、カメラそのものではなく手前の
    /// クリップ面の上に来ます。それより手前は映らないので、掴む判定には影響しません。
    /// </param>
    /// <param name="Direction">向き（単位ベクトル）。</param>
    public readonly record struct PickRay(Vector3 Origin, Vector3 Direction)
    {
        /// <summary>
        /// 画面上の位置から半直線を作ります。
        /// </summary>
        /// <param name="position">描画先の左上を原点としたピクセル位置。</param>
        /// <param name="width">描画先の幅（ピクセル）。</param>
        /// <param name="height">描画先の高さ（ピクセル）。</param>
        /// <param name="viewProjection">描画に使ったビュー行列と射影行列の積。</param>
        public static PickRay? FromScreen(
            Vector2 position, float width, float height, in Matrix4x4 viewProjection)
        {
            if (width <= 0f || height <= 0f || !Matrix4x4.Invert(viewProjection, out var inverse))
                return null;

            // 画面の座標を NDC へ。Y は画面が下向き、NDC が上向きなので反転する。
            var ndc = new Vector2(
                position.X / width * 2f - 1f,
                1f - position.Y / height * 2f);

            // 手前と奥、2つの面に打った点を戻すと、その2点を通る線が視線になる。
            var near = Unproject(new Vector3(ndc, 0f), inverse);
            var far = Unproject(new Vector3(ndc, 1f), inverse);

            if (near is not { } start || far is not { } end)
                return null;

            var direction = end - start;

            return direction.LengthSquared() > 0f
                ? new PickRay(start, Vector3.Normalize(direction))
                : null;
        }

        /// <summary>
        /// 中心が原点、一辺が 1 の立方体との交点までの距離を返します。
        /// </summary>
        /// <param name="world">立方体に掛かっている変換。</param>
        /// <param name="minThickness">
        /// 厚みの下限。板のように潰れた形は厚みが 0 になり、そのままでは掴めません。
        /// </param>
        /// <remarks>
        /// 半直線の方を変換の逆で引き戻し、軸に沿った箱として調べます。回転や
        /// 拡大が掛かっていても、箱の側を作り直さずに済みます。
        /// </remarks>
        public float? IntersectUnitBox(in Matrix4x4 world, float minThickness = 0.001f)
        {
            if (!Matrix4x4.Invert(world, out var inverse))
                return null;

            var origin = Vector3.Transform(Origin, inverse);
            var direction = Vector3.TransformNormal(Direction, inverse);

            var half = Vector3.Max(new Vector3(0.5f), new Vector3(minThickness / 2f));

            var enter = float.NegativeInfinity;
            var exit = float.PositiveInfinity;

            for (var axis = 0; axis < 3; axis++)
            {
                var slope = Component(direction, axis);
                var start = Component(origin, axis);
                var limit = Component(half, axis);

                if (MathF.Abs(slope) < 1e-9f)
                {
                    // その軸に平行。箱の外を通っているならどこまで行っても当たらない。
                    if (MathF.Abs(start) > limit)
                        return null;

                    continue;
                }

                var first = (-limit - start) / slope;
                var second = (limit - start) / slope;

                if (first > second)
                    (first, second) = (second, first);

                enter = MathF.Max(enter, first);
                exit = MathF.Min(exit, second);

                if (enter > exit)
                    return null;
            }

            if (exit < 0f)
                return null;

            // 始点が箱の中にあるときは、入る所ではなく始点そのものが最も近い。
            var local = MathF.Max(enter, 0f);

            // 引き戻した空間での距離なので、元の空間での距離に直す。
            var hit = Vector3.Transform(origin + direction * local, world);

            return Vector3.Distance(Origin, hit);
        }

        /// <summary>
        /// 平面との交点を返します。
        /// </summary>
        /// <param name="point">平面上の1点。</param>
        /// <param name="normal">平面の法線。</param>
        /// <remarks>
        /// 掴んだアイテムを画面に沿って動かすのに使います。視線に垂直な平面を置けば、
        /// マウスの動きがそのまま奥行きを変えない移動になります。
        /// </remarks>
        public Vector3? IntersectPlane(in Vector3 point, in Vector3 normal)
        {
            var slope = Vector3.Dot(Direction, normal);

            if (MathF.Abs(slope) < 1e-6f)
                return null;

            var distance = Vector3.Dot(point - Origin, normal) / slope;

            return distance > 0f ? Origin + Direction * distance : null;
        }

        private static Vector3? Unproject(in Vector3 ndc, in Matrix4x4 inverse)
        {
            var point = Vector4.Transform(new Vector4(ndc, 1f), inverse);

            if (MathF.Abs(point.W) < 1e-9f)
                return null;

            return new Vector3(point.X, point.Y, point.Z) / point.W;
        }

        private static float Component(in Vector3 value, int axis)
            => axis switch { 0 => value.X, 1 => value.Y, _ => value.Z };
    }
}
