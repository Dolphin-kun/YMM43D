using System;
using Vortice.Direct3D11;

namespace YMM43D.Rendering.States
{
    public class DepthStencilStates : IDisposable
    {
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

            NoDepth = device.CreateDepthStencilState(new DepthStencilDescription
            {
                DepthEnable = false,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunction.Always
            });
        }

        public void Dispose()
        {
            NoDepth.Dispose();
            Default.Dispose();
        }
    }
}
