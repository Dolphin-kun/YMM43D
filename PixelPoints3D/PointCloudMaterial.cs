using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YukkuriMovieMaker.Commons;

namespace PixelPoints3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointCloudConstants
    {
        public Vector4 Color;

        public Vector3 GridCount;

        public float Threshold;

        public Vector3 Extent;

        public float Seed;

        public Vector3 Scatter;

        public float PointHalfSize;

        public Vector3 ViewRight;

        public float LineHalfWidth;

        public Vector3 ViewUp;

        public float UseSourceColor;

        public Vector3 ViewForward;

        public float ExtraOpacity;

        public Vector3 DeformAxis;

        public float DeformKind;

        public float DeformAmount;

        public float DeformPeriod;

        public float DeformPhase;

        public float LineRandomness;

        public float OpacityRandomness;

        public float PointIsRound;

        private readonly float padding0;

        private readonly float padding1;
    }

    internal sealed class PointCloudMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        private const string VertexShader3D = "PointCloudVertex.hlsl";

        private const string PixelShader3D = "PointCloudPixel.hlsl";

        public PointCloudMaterial(ID3D11Device device)
        {
            var assembly = typeof(PointCloudMaterial).Assembly;

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
