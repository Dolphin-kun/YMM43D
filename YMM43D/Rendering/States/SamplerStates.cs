using System;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Rendering.States
{
    public class SamplerStates : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11SamplerState Linear { get; }
        public ID3D11SamplerState Point { get; }

        public SamplerStates(ID3D11Device device)
        {
            Linear = device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = ComparisonFunction.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue
            });
            disposer.Collect(Linear);

            Point = device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipPoint,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = ComparisonFunction.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue
            });
            disposer.Collect(Point);
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
