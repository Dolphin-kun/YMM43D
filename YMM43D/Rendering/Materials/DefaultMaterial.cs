using Vortice.D3DCompiler;
using Vortice.Direct3D11;

namespace YMM43D.Rendering.Materials
{
    public class DefaultMaterial : I3DMaterial
    {
        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        public DefaultMaterial(ID3D11Device device)
        {
            string shaderSource = @"
                cbuffer TransformBuffer : register(b0) { 
                    matrix WorldViewProjection; 
                    float Opacity;
                    float3 Padding;
                };
                struct VS_IN { float3 Pos : POSITION; float4 Col : COLOR; float2 Tex : TEXCOORD; };
                struct PS_IN { float4 Pos : SV_POSITION; float4 Col : COLOR; float2 Tex : TEXCOORD; };
                PS_IN VSMain(VS_IN input) {
                    PS_IN output;
                    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
                    output.Col = input.Col;
                    output.Tex = input.Tex;
                    return output;
                }
                float4 PSMain(PS_IN input) : SV_TARGET { 
                    float4 col = input.Col;
                    col.a *= Opacity; // 不透明度を反映
                    return col; 
                }
            ";

            var vsBlob = Compiler.Compile(shaderSource, "VSMain", "DefaultShader", "vs_5_0");
            var psBlob = Compiler.Compile(shaderSource, "PSMain", "DefaultShader", "ps_5_0");

            VertexShader = device.CreateVertexShader(vsBlob);
            PixelShader = device.CreatePixelShader(psBlob);

            VertexShaderBytecode = vsBlob.AsBytes();

            vsBlob.Dispose();
            psBlob.Dispose();
        }

        public void Dispose()
        {
            VertexShader?.Dispose();
            PixelShader?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
