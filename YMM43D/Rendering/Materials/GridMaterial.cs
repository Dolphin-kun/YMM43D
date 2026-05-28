using System;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Rendering.Materials
{
    public class GridMaterial : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        public GridMaterial(ID3D11Device device)
        {
            string shaderSource = @"
                cbuffer TransformBuffer : register(b0) { 
                    matrix WorldViewProjection; 
                    float4 CameraPos; 
                };
                struct VS_IN { float3 Pos : POSITION; float4 Col : COLOR; float2 Tex : TEXCOORD; };
                struct PS_IN { float4 Pos : SV_POSITION; float3 WorldPos : WORLDPOS; };

                PS_IN VSMain(VS_IN input) {
                    PS_IN output;
                    output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
                    output.WorldPos = input.Pos;
                    return output;
                }

                float GridLine(float position, float width) {
                    float viewPos = abs(frac(position - 0.5) - 0.5);
                    float lineValue = smoothstep(0, width, viewPos);
                    return 1.0 - lineValue;
                }

                float4 PSMain(PS_IN input) : SV_TARGET {
                    float3 pos = input.WorldPos;
                    float dist = length(pos.xz - CameraPos.xz);
                    
                    float grid1 = GridLine(pos.x, 0.03) + GridLine(pos.z, 0.03);
                    
                    float4 color = float4(0.2, 0.2, 0.2, 1.0); 
                    
                    if (abs(pos.z) < 0.05) color = float4(0.8, 0.1, 0.1, 1.0); // X軸 (赤)
                    else if (abs(pos.x) < 0.05) color = float4(0.15, 0.35, 0.95, 1.0); // Z軸 (少し明るい青に変更)
                    
                    float alpha = grid1;
                    if (abs(pos.z) < 0.05 || abs(pos.x) < 0.05) alpha = 1.0;

                    float fade = 1.0 - smoothstep(10.0, 100.0, dist);
                    if (alpha <= 0.0) discard;
                    
                    return float4(color.rgb, alpha * fade * 0.5);
                }
            ";

            using var vsBlob = Compiler.Compile(shaderSource, "VSMain", "GridShader", "vs_5_0");
            using var psBlob = Compiler.Compile(shaderSource, "PSMain", "GridShader", "ps_5_0");

            VertexShader = device.CreateVertexShader(vsBlob);
            disposer.Collect(VertexShader);
            PixelShader = device.CreatePixelShader(psBlob);
            disposer.Collect(PixelShader);
            VertexShaderBytecode = vsBlob.AsBytes();
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
