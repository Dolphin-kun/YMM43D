using System.Numerics;
using System.Runtime.InteropServices;

namespace YMM43D.Graphics
{
    /// <summary>
    /// ワールド・ビュー・射影の合成行列と不透明度だけを持つ、最小構成の定数バッファ。
    /// <see cref="Materials.VertexColorMaterial"/> と <see cref="Materials.TextureMaterial"/>
    /// が使います。
    /// </summary>
    /// <remarks>
    /// HLSL 側の宣言は次のとおりです。<c>float3</c> のパディングは
    /// 定数バッファが 16 バイト境界に揃うようにするためのものです。
    /// <code>
    /// cbuffer TransformBuffer : register(b0)
    /// {
    ///     matrix WorldViewProjection;
    ///     float  Opacity;
    ///     float3 Padding;
    /// };
    /// </code>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct TransformConstants
    {
        /// <summary>
        /// HLSL に渡す合成行列。列優先に変換するため
        /// <see cref="Matrix4x4.Transpose"/> した値を入れてください。
        /// </summary>
        public Matrix4x4 WorldViewProjection;

        /// <summary>0.0〜1.0 の不透明度。</summary>
        public float Opacity;

        private Vector3 padding;

        /// <summary>
        /// 転置と不透明度の設定をまとめて行います。
        /// </summary>
        public static TransformConstants Create(Matrix4x4 worldViewProjection, float opacity) => new()
        {
            WorldViewProjection = Matrix4x4.Transpose(worldViewProjection),
            Opacity = opacity,
        };
    }
}
