using Vortice.Direct3D11;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace Deform3D
{
    internal sealed class DeformMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }

        public ID3D11PixelShader PixelShader { get; }

        public byte[] VertexShaderBytecode { get; }

        public DeformMaterial(ID3D11Device device, string shader)
        {
            var assembly = typeof(DeformMaterial).Assembly;

            VertexShaderBytecode = ShaderLibrary.Compile(assembly, shader, "VSMain", "vs_5_0");
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            PixelShader = device.CreatePixelShader(
                ShaderLibrary.Compile(assembly, shader, "PSMain", "ps_5_0"));
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
