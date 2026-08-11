using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Materials
{
    /// <summary>
    /// 頂点カラーをそのまま出力し、不透明度だけを掛けるシェーダー。
    /// テクスチャを持たない形状（3D図形やガイド表示）に使います。
    /// 定数バッファは <see cref="TransformConstants"/> です。
    /// </summary>
    public sealed class VertexColorMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        public VertexColorMaterial(ID3D11Device device)
        {
            var source = ShaderSource.StandardPrologue + """
                float4 PSMain(PS_IN input) : SV_TARGET
                {
                    float4 col = input.Col;
                    col.a *= Opacity;
                    return col;
                }
                """;

            VertexShaderBytecode = ShaderCompiler.Compile(source, "VSMain", "vs_5_0", nameof(VertexColorMaterial));
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            var pixelShaderBytecode = ShaderCompiler.Compile(source, "PSMain", "ps_5_0", nameof(VertexColorMaterial));
            PixelShader = device.CreatePixelShader(pixelShaderBytecode);
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
