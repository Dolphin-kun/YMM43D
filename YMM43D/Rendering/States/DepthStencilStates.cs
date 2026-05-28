using System;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Rendering.States
{
    public class DepthStencilStates : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11DepthStencilState Default { get; }
        public ID3D11DepthStencilState NoDepth { get; }

        public DepthStencilStates(ID3D11Device device)
        {
            Default = device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthFunc = ComparisonFunction.LessEqual
            });
            disposer.Collect(Default);

            NoDepth = device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = false,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunction.Always
            });
            disposer.Collect(NoDepth);
        }

        public void Dispose()
        {
            disposer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
