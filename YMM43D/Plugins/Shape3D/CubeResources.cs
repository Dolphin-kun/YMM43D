using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Rendering;
using YMM43D.Rendering.Geometries;
using YMM43D.Rendering.Materials;
using YMM43D.Rendering.States;

namespace YMM43D.Plugins.Shape3D
{
    internal class CubeResources : IDisposable
    {
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
            Material = new DefaultMaterial(device);
            InputLayout = device.CreateInputLayout(Geometry.InputElements, Material.VertexShaderBytecode);
            ConstantBuffer = D3D11Helper.CreateConstantBuffer<ConstantData>(device);

            BlendStates = new DeviceResourceCache<BlendStates>(d => new BlendStates(d));
            DepthStencilStates = new DeviceResourceCache<DepthStencilStates>(d => new DepthStencilStates(d));
            RasterizerStates = new DeviceResourceCache<RasterizerStates>(d => new RasterizerStates(d));
        }

        public void Dispose()
        {
            RasterizerStates.Dispose();
            BlendStates.Dispose();
            DepthStencilStates.Dispose();
            ConstantBuffer.Dispose();
            InputLayout.Dispose();
            Material.Dispose();
            Geometry.Dispose();
        }
    }
}
