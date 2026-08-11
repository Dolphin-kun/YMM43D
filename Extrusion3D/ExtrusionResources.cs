using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Rendering;
using YMM43D.Rendering.States;
using YMM43D.Commons;

namespace Extrusion3D
{
    /// <summary>
    /// Extrusion3D エフェクトで使用する GPU リソースをまとめて管理するクラス。
    /// デバイスごとにキャッシュされる。
    /// </summary>
    internal class ExtrusionResources : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ConstantData
        {
            public Matrix4x4 WorldViewProjection;
            public Vector4 SideColor;
            public Vector3 CameraLocalPos;
            public float Opacity;
            public int ExtrusionType;
            public float Attenuation;
            private Vector2 padding;
        }

        public I3DGeometry Geometry { get; }
        public I3DMaterial Material { get; }
        public ID3D11InputLayout InputLayout { get; }
        public ID3D11Buffer ConstantBuffer { get; }
        public ID3D11SamplerState SamplerState { get; }
        public BlendStates BlendStates { get; }
        public DepthStencilStates DepthStencilStates { get; }
        public RasterizerStates RasterizerStates { get; }

        public ExtrusionResources(ID3D11Device device)
        {
            Geometry = new ExtrusionGeometry(device);
            Material = new ExtrusionMaterial(device);
            InputLayout = device.CreateInputLayout(Geometry.InputElements, Material.VertexShaderBytecode);
            ConstantBuffer = D3D11Helper.CreateConstantBuffer<ConstantData>(device);
            SamplerState = device.CreateSamplerState(new SamplerDescription {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
            });

            BlendStates = new BlendStates(device);
            DepthStencilStates = new DepthStencilStates(device);
            RasterizerStates = new RasterizerStates(device);
        }

        public void Dispose()
        {
            SamplerState.Dispose();
            ConstantBuffer.Dispose();
            InputLayout.Dispose();
            Material.Dispose();
            Geometry.Dispose();
            BlendStates.Dispose();
            DepthStencilStates.Dispose();
            RasterizerStates.Dispose();
        }
    }
}
