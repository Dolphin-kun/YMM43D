using System.Numerics;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// 描くものがワールド空間で占める、軸に沿った直方体の範囲。
    /// </summary>
    /// <remarks>
    /// 出力画像の大きさを決めるのに使います。単なる差し渡しの長さではなく範囲を
    /// 受け取るのは、遠近によって手前の面ほど大きく映るためです。8つの隅を実際に
    /// 投影しないと、必要な描画先の大きさを正しく見積もれません。
    /// </remarks>
    public readonly record struct WorldBounds(Vector3 Min, Vector3 Max)
    {
        /// <summary>何も含まない範囲。</summary>
        public static WorldBounds Empty => new(Vector3.Zero, Vector3.Zero);

        /// <summary>原点を中心とする立方体。</summary>
        /// <param name="edgeLength">一辺の長さ。</param>
        public static WorldBounds FromCube(float edgeLength)
        {
            var half = new Vector3(edgeLength / 2f);
            return new WorldBounds(-half, half);
        }

        /// <summary>原点を中心とする直方体。</summary>
        public static WorldBounds FromSize(Vector3 size)
        {
            var half = size / 2f;
            return new WorldBounds(-half, half);
        }

        /// <summary>大きさが無く、描くものが存在しないかどうか。</summary>
        public bool IsEmpty => Max.X <= Min.X || Max.Y <= Min.Y;

        /// <summary>8つの隅の座標を返します。</summary>
        public Vector3[] GetCorners() =>
        [
            new(Min.X, Min.Y, Min.Z),
            new(Max.X, Min.Y, Min.Z),
            new(Min.X, Max.Y, Min.Z),
            new(Max.X, Max.Y, Min.Z),
            new(Min.X, Min.Y, Max.Z),
            new(Max.X, Min.Y, Max.Z),
            new(Min.X, Max.Y, Max.Z),
            new(Max.X, Max.Y, Max.Z),
        ];

        /// <summary>この範囲に変換を掛けた結果を含む、軸に沿った範囲を返します。</summary>
        public WorldBounds Transform(in Matrix4x4 matrix)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var corner in GetCorners())
            {
                var moved = Vector3.Transform(corner, matrix);
                min = Vector3.Min(min, moved);
                max = Vector3.Max(max, moved);
            }

            return new WorldBounds(min, max);
        }
    }
}
