namespace YMM43D.Graphics.Materials
{
    public static class ShaderSource
    {
        public const string TransformBuffer = """
            cbuffer TransformBuffer : register(b0)
            {
                matrix WorldViewProjection;
                float  Opacity;
                float3 Padding;
            };
            """;

        public const string VertexInput = """
            struct VS_IN
            {
                float3 Pos : POSITION;
                float4 Col : COLOR;
                float2 Tex : TEXCOORD;
            };
            """;

        public const string PixelInput = """
            struct PS_IN
            {
                float4 Pos : SV_POSITION;
                float4 Col : COLOR;
                float2 Tex : TEXCOORD;
            };
            """;

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

        public static string StandardPrologue =>
            $"{TransformBuffer}\n{VertexInput}\n{PixelInput}\n{VertexShaderMain}\n";
    }
}
