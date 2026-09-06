using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Materials
{
    public sealed class VertexColorMaterial : IMaterial
    {
        private const string Shader = "VertexColorMaterial.hlsl";

        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        public VertexColorMaterial(ID3D11Device device)
        {
            var assembly = typeof(VertexColorMaterial).Assembly;

            VertexShaderBytecode = ShaderLibrary.Compile(assembly, Shader, "VSMain", "vs_5_0");
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            PixelShader = device.CreatePixelShader(
                ShaderLibrary.Compile(assembly, Shader, "PSMain", "ps_5_0"));
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
