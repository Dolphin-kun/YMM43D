using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Rendering;
using YMM43D.Rendering.Geometries;
using YMM43D.Rendering.Materials;
using YMM43D.Rendering.States;
using YukkuriMovieMaker.Commons;
using YMM43D.Commons;

namespace Shape3D
{
    internal class CubeResources : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        [StructLayout(LayoutKind.Sequential)]
        public struct ConstantData
        {
            public Matrix4x4 WorldViewProjection;
            public float Opacity;
            private Vector3 padding;
        }

        public I3DGeometry Geometry { get; }
        public I3DMaterial Material { get; }
        public ID3D11InputLayout InputLayout { get; }
        public ID3D11Buffer ConstantBuffer { get; }

        public DeviceResourceCache<BlendStates> BlendStates { get; }
        public DeviceResourceCache<DepthStencilStates> DepthStencilStates { get; }
        public DeviceResourceCache<RasterizerStates> RasterizerStates { get; }

        public CubeResources(ID3D11Device device)
        {
            Geometry = new CubeGeometry(device);
            disposer.Collect(Geometry);
            Material = new DefaultMaterial(device);
            disposer.Collect(Material);
            InputLayout = device.CreateInputLayout(Geometry.InputElements, Material.VertexShaderBytecode);
            disposer.Collect(InputLayout);
            ConstantBuffer = D3D11Helper.CreateConstantBuffer<ConstantData>(device);
            disposer.Collect(ConstantBuffer);

            BlendStates = new DeviceResourceCache<BlendStates>(d => new BlendStates(d));
            disposer.Collect(BlendStates);
            DepthStencilStates = new DeviceResourceCache<DepthStencilStates>(d => new DepthStencilStates(d));
            disposer.Collect(DepthStencilStates);
            RasterizerStates = new DeviceResourceCache<RasterizerStates>(d => new RasterizerStates(d));
            disposer.Collect(RasterizerStates);
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
