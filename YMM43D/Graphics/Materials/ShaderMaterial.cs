using System.Reflection;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Materials
{
    public class ShaderMaterial : IMaterial
    {
        private const string VertexProfile = "vs_5_0";

        private const string PixelProfile = "ps_5_0";

        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }

        public ID3D11PixelShader PixelShader { get; }

        public byte[] VertexShaderBytecode { get; }

        public ShaderMaterial(ID3D11Device device, Assembly assembly, string shader)
            : this(device, assembly, shader, "VSMain", shader, "PSMain")
        {
        }

        public ShaderMaterial(
            ID3D11Device device,
            Assembly assembly,
            string vertexShader,
            string vertexEntryPoint,
            string pixelShader,
            string pixelEntryPoint)
        {
            VertexShaderBytecode = ShaderLibrary.Compile(assembly, vertexShader, vertexEntryPoint, VertexProfile);
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            PixelShader = device.CreatePixelShader(
                ShaderLibrary.Compile(assembly, pixelShader, pixelEntryPoint, PixelProfile));
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
