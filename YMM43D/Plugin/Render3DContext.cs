using System.Numerics;
using Vortice.Direct3D11;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D描画1回分の描画先とカメラ行列。
    /// </summary>
    /// <remarks>
    /// デバイス・コンテキスト・ビュー行列・射影行列の4つは常にセットで受け渡されるため、
    /// 1つの型にまとめています。
    /// </remarks>
    public readonly record struct Render3DContext(
        ID3D11Device Device,
        ID3D11DeviceContext Context,
        Matrix4x4 View,
        Matrix4x4 Projection)
    {
        /// <summary>ビュー行列と射影行列の積。</summary>
        public Matrix4x4 ViewProjection => View * Projection;

        /// <summary>
        /// ワールド行列を掛けた最終的な変換行列を返します。
        /// </summary>
        public Matrix4x4 GetWorldViewProjection(in Matrix4x4 world) => world * View * Projection;

        /// <summary>
        /// ワールド空間でのカメラ位置。ビュー行列の逆行列から求めます。
        /// </summary>
        public Vector3 GetCameraPosition()
        {
            Matrix4x4.Invert(View, out var inverse);
            return inverse.Translation;
        }
    }
}
