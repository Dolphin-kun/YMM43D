using Vortice.Direct3D11;

namespace YMM43D.Rendering.States
{
    public class SamplerStates(ID3D11Device device)
    {
        public ID3D11SamplerState Linear { get; } = device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunction = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });

        public ID3D11SamplerState Point { get; } = device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunction = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });

        public void Dispose()
        {
            Linear.Dispose();
            Point.Dispose();
        }
    }
}
