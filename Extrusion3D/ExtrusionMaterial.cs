using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YukkuriMovieMaker.Commons;

namespace Extrusion3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtrusionConstants
    {
        public TransformConstants Transform;

        public Vector4 SideColor;

        public Vector3 CameraLocalPos;

        public int ExtrusionType;
    }

    internal sealed class ExtrusionMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        private const string VertexShader3D = "ExtrusionVertex.hlsl";

        private const string PixelShader3D = "ExtrusionPixel.hlsl";

        public ExtrusionMaterial(ID3D11Device device)
        {
            var assembly = typeof(ExtrusionMaterial).Assembly;

            VertexShaderBytecode = ShaderLibrary.Compile(assembly, VertexShader3D, "VSMain", "vs_5_0");
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            PixelShader = device.CreatePixelShader(
                ShaderLibrary.Compile(assembly, PixelShader3D, "main", "ps_5_0"));
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
