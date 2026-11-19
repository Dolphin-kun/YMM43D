using System.Reflection;
using Vortice.Direct3D11;
using YMM43D.Commons;
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

        public ShaderMaterial(ID3D11Device device, Assembly assembly, string shader, string? prelude)
            : this(device, assembly, shader, "VSMain", shader, "PSMain", prelude)
        {
        }

        public ShaderMaterial(
            ID3D11Device device,
            Assembly assembly,
            string vertexShader,
            string vertexEntryPoint,
            string pixelShader,
            string pixelEntryPoint,
            string? prelude = null)
        {
            VertexShaderBytecode = ShaderLibrary.Compile(assembly, vertexShader, vertexEntryPoint, VertexProfile, prelude);
            VertexShader = disposer.CollectEx(device.CreateVertexShader(VertexShaderBytecode));

            PixelShader = disposer.CollectEx(device.CreatePixelShader(ShaderLibrary.Compile(assembly, pixelShader, pixelEntryPoint, PixelProfile, prelude)));
        }

        public void Dispose()
        {
            disposer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
