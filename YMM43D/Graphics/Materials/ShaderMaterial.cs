using System.Reflection;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Materials
{
    // hlsl はプラグイン側のアセンブリに埋まっているので、そちらを渡してもらう。
    // その中の #include は、見つからなければ YMM43D から拾われる。
    public class ShaderMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }

        public ID3D11PixelShader PixelShader { get; }

        public byte[] VertexShaderBytecode { get; }

        public ShaderMaterial(ID3D11Device device, Assembly assembly, string shader)
        {
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
