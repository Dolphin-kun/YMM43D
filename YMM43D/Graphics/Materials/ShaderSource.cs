namespace YMM43D.Graphics.Materials
{
    /// <summary>
    /// 標準頂点フォーマット向けの HLSL 断片。
    /// シェーダーを自作する際に、入出力構造体と定数バッファの宣言を使い回せます。
    /// </summary>
    public static class ShaderSource
    {
        /// <summary>
        /// <see cref="TransformConstants"/> に対応する <c>cbuffer</c> 宣言。
        /// </summary>
        public const string TransformBuffer = """
            cbuffer TransformBuffer : register(b0)
            {
                matrix WorldViewProjection;
                float  Opacity;
                float3 Padding;
            };
            """;

        /// <summary>
        /// <see cref="Vertex"/> に対応する頂点シェーダーの入力構造体。
        /// </summary>
        public const string VertexInput = """
            struct VS_IN
            {
                float3 Pos : POSITION;
                float4 Col : COLOR;
                float2 Tex : TEXCOORD;
            };
            """;

        /// <summary>
        /// 位置・色・UV をそのまま渡す、頂点シェーダーからの出力構造体。
        /// </summary>
        public const string PixelInput = """
            struct PS_IN
            {
                float4 Pos : SV_POSITION;
                float4 Col : COLOR;
                float2 Tex : TEXCOORD;
            };
            """;

        /// <summary>
        /// 座標変換のみを行う標準の頂点シェーダー本体。エントリポイントは <c>VSMain</c>。
        /// </summary>
        public const string VertexShaderMain = """
            PS_IN VSMain(VS_IN input)
            {
                PS_IN output;
                output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
                output.Col = input.Col;
                output.Tex = input.Tex;
                return output;
            }
            """;

        /// <summary>
        /// 上記の宣言をすべて連結した、標準的な頂点シェーダーの前置き。
        /// </summary>
        public static string StandardPrologue =>
            $"{TransformBuffer}\n{VertexInput}\n{PixelInput}\n{VertexShaderMain}\n";
    }
}
