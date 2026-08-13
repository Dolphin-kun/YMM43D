using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Materials
{
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
                    return Shade(input.Col, input);
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
