using System;
using System.Collections.Generic;
using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;
using YMM43D.Commons;

namespace YMM43D.Preview.Views
{
    internal class CameraResources : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11Buffer VertexBuffer { get; }
        public int VertexCount { get; }
        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public ID3D11InputLayout InputLayout { get; }
        public ID3D11Buffer ConstantBuffer { get; }

        public CameraResources(ID3D11Device device)
        {
            var white = new Color4(1, 1, 1, 1);
            
            // シンプルなカメラ形状（本体ボックス + 前方レンズピラミッド）
            // カメラは-Z方向を向いていると想定
            var bodyMin = new Vector3(-0.4f, -0.3f, 0.0f);
            var bodyMax = new Vector3(0.4f, 0.3f, 0.8f);
            var lensFront = new Vector3[] {
                new(-0.8f, 0.6f, -1.2f),
                new(0.8f, 0.6f, -1.2f),
                new(0.8f, -0.6f, -1.2f),
                new(-0.8f, -0.6f, -1.2f)
            };

            var lineList = new List<Vector3>();
            
            // 1. 本体ボックス (8点から12辺)
            Vector3[] b = [
                new(bodyMin.X, bodyMin.Y, bodyMin.Z), new(bodyMax.X, bodyMin.Y, bodyMin.Z),
                new(bodyMax.X, bodyMax.Y, bodyMin.Z), new(bodyMin.X, bodyMax.Y, bodyMin.Z),
                new(bodyMin.X, bodyMin.Y, bodyMax.Z), new(bodyMax.X, bodyMin.Y, bodyMax.Z),
                new(bodyMax.X, bodyMax.Y, bodyMax.Z), new(bodyMin.X, bodyMax.Y, bodyMax.Z)
            ];
            // 底面
            lineList.Add(b[0]); lineList.Add(b[1]); lineList.Add(b[1]); lineList.Add(b[2]);
            lineList.Add(b[2]); lineList.Add(b[3]); lineList.Add(b[3]); lineList.Add(b[0]);
            // 天面
            lineList.Add(b[4]); lineList.Add(b[5]); lineList.Add(b[5]); lineList.Add(b[6]);
            lineList.Add(b[6]); lineList.Add(b[7]); lineList.Add(b[7]); lineList.Add(b[4]);
            // 柱
            lineList.Add(b[0]); lineList.Add(b[4]); lineList.Add(b[1]); lineList.Add(b[5]);
            lineList.Add(b[2]); lineList.Add(b[6]); lineList.Add(b[3]); lineList.Add(b[7]);

            // 2. レンズ部分 (本体前面 Z=0 からレンズ先端 Z=-1.2 へ)
            // 四隅
            lineList.Add(b[0]); lineList.Add(lensFront[3]);
            lineList.Add(b[1]); lineList.Add(lensFront[2]);
            lineList.Add(b[2]); lineList.Add(lensFront[1]);
            lineList.Add(b[3]); lineList.Add(lensFront[0]);
            // レンズ前面枠
            lineList.Add(lensFront[0]); lineList.Add(lensFront[1]);
            lineList.Add(lensFront[1]); lineList.Add(lensFront[2]);
            lineList.Add(lensFront[2]); lineList.Add(lensFront[3]);
            lineList.Add(lensFront[3]); lineList.Add(lensFront[0]);

            var finalVertices = new Vertex[lineList.Count];
            for (int i = 0; i < lineList.Count; i++)
                finalVertices[i] = new Vertex(lineList[i], white, Vector2.Zero);

            VertexCount = finalVertices.Length;
            VertexBuffer = D3D11Helper.CreateBuffer(device, finalVertices, BindFlags.VertexBuffer);
            disposer.Collect(VertexBuffer);

            var vsCode = @"
                cbuffer cb : register(b0) { float4x4 wvp; };
                struct VS_IN { float3 pos : POSITION; };
                struct VS_OUT { float4 pos : SV_POSITION; };
                VS_OUT main(VS_IN input) {
                    VS_OUT output;
                    output.pos = mul(float4(input.pos, 1.0), wvp);
                    return output;
                }
            ";
            var psCode = @"
                float4 main() : SV_TARGET { return float4(1, 0.6, 0, 1); }
            ";

            var vsByteCode = D3D11Helper.CompileShader(vsCode, "main", "vs_5_0");
            VertexShader = device.CreateVertexShader(vsByteCode);
            disposer.Collect(VertexShader);
            var psByteCode = D3D11Helper.CompileShader(psCode, "main", "ps_5_0");
            PixelShader = device.CreatePixelShader(psByteCode);
            disposer.Collect(PixelShader);

            InputLayout = device.CreateInputLayout(new[] {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)
            }, vsByteCode);
            disposer.Collect(InputLayout);

            ConstantBuffer = D3D11Helper.CreateConstantBuffer<Matrix4x4>(device);
            disposer.Collect(ConstantBuffer);
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
