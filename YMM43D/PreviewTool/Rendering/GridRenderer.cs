using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool.Rendering
{
    /// <summary>
    /// 3Dプレビューの床面グリッドと座標軸。
    /// </summary>
    /// <remarks>
    /// 実際の格子はピクセルシェーダーで描くため、形状は巨大な四角形1枚だけです。
    /// </remarks>
    internal sealed class GridRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct GridConstants
        {
            public Matrix4x4 WorldViewProjection;
            public Vector4 CameraPosition;
        }

        private readonly DeviceResourceCache<RenderPipeline<GridConstants>> pipelines;

        public GridRenderer()
        {
            pipelines = new DeviceResourceCache<RenderPipeline<GridConstants>>(
                device => new RenderPipeline<GridConstants>(
                    device,
                    new GroundPlaneMesh(device),
                    new GridMaterial(device)));
        }

        public void Draw(in Render3DContext render, Vector3 cameraPosition)
        {
            var constants = new GridConstants
            {
                WorldViewProjection = Matrix4x4.Transpose(render.ViewProjection),
                CameraPosition = new Vector4(cameraPosition, 0),
            };

            pipelines.Get(render.Device).Draw(render.Context, constants, new DrawSettings
            {
                Blend = BlendMode.Normal,
                Culling = FaceCulling.None,
            });
        }

        public void Dispose() => pipelines.Dispose();
    }

    /// <summary>
    /// 床面に格子と座標軸を描くシェーダー。
    /// </summary>
    internal sealed class GridMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        private const string Source = """
            cbuffer GridConstants : register(b0)
            {
                matrix WorldViewProjection;
                float4 CameraPosition;
            };

            struct VS_IN  { float3 Pos : POSITION; float4 Col : COLOR; float2 Tex : TEXCOORD; };
            struct PS_IN  { float4 Pos : SV_POSITION; float3 WorldPos : WORLDPOS; };

            PS_IN VSMain(VS_IN input)
            {
                PS_IN output;
                output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
                output.WorldPos = input.Pos;
                return output;
            }

            // 1単位ごとの格子線を、線幅 width で描いたときの濃さを返す
            float GridLine(float position, float width)
            {
                float distanceToLine = abs(frac(position - 0.5) - 0.5);
                return 1.0 - smoothstep(0, width, distanceToLine);
            }

            float4 PSMain(PS_IN input) : SV_TARGET
            {
                float3 pos = input.WorldPos;

                float alpha = GridLine(pos.x, 0.03) + GridLine(pos.z, 0.03);
                float4 color = float4(0.2, 0.2, 0.2, 1.0);

                // 原点を通る2本は座標軸として色を変え、常に不透明にする
                bool onXAxis = abs(pos.z) < 0.05;
                bool onZAxis = abs(pos.x) < 0.05;
                if (onXAxis)      color = float4(0.8, 0.1, 0.1, 1.0);
                else if (onZAxis) color = float4(0.15, 0.35, 0.95, 1.0);
                if (onXAxis || onZAxis) alpha = 1.0;

                if (alpha <= 0.0) discard;

                // 遠くほど薄くして、地平線付近のちらつきを抑える
                float distance = length(pos.xz - CameraPosition.xz);
                float fade = 1.0 - smoothstep(10.0, 100.0, distance);

                return float4(color.rgb, alpha * fade * 0.5);
            }
            """;

        public GridMaterial(ID3D11Device device)
        {
            VertexShaderBytecode = ShaderCompiler.Compile(Source, "VSMain", "vs_5_0", nameof(GridMaterial));
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            var pixelShaderBytecode = ShaderCompiler.Compile(Source, "PSMain", "ps_5_0", nameof(GridMaterial));
            PixelShader = device.CreatePixelShader(pixelShaderBytecode);
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
