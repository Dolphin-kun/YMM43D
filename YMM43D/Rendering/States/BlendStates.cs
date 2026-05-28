using System;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Rendering.States
{
    public class BlendStates : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11BlendState Normal { get; }
        public ID3D11BlendState Add { get; }
        public ID3D11BlendState Subtract { get; }
        public ID3D11BlendState Multiply { get; }
        public ID3D11BlendState Screen { get; }

        public BlendStates(ID3D11Device device)
        {
            var blendDesc = new BlendDescription();
            blendDesc.RenderTarget[0].IsBlendEnabled = true;
            blendDesc.RenderTarget[0].RenderTargetWriteMask = ColorWriteEnable.All;

            blendDesc.RenderTarget[0].SourceBlend = Blend.SourceAlpha;
            blendDesc.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
            blendDesc.RenderTarget[0].BlendOperation = BlendOperation.Add;
            blendDesc.RenderTarget[0].SourceBlendAlpha = Blend.One;
            blendDesc.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
            blendDesc.RenderTarget[0].BlendOperationAlpha = BlendOperation.Add;
            Normal = device.CreateBlendState(blendDesc);
            disposer.Collect(Normal);

            blendDesc.RenderTarget[0].SourceBlend = Blend.SourceAlpha;
            blendDesc.RenderTarget[0].DestinationBlend = Blend.One;
            Add = device.CreateBlendState(blendDesc);
            disposer.Collect(Add);

            blendDesc.RenderTarget[0].SourceBlend = Blend.SourceAlpha;
            blendDesc.RenderTarget[0].DestinationBlend = Blend.One;
            blendDesc.RenderTarget[0].BlendOperation = BlendOperation.ReverseSubtract;
            Subtract = device.CreateBlendState(blendDesc);
            disposer.Collect(Subtract);

            blendDesc.RenderTarget[0].SourceBlend = Blend.DestinationColor;
            blendDesc.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
            blendDesc.RenderTarget[0].BlendOperation = BlendOperation.Add;
            Multiply = device.CreateBlendState(blendDesc);
            disposer.Collect(Multiply);

            blendDesc.RenderTarget[0].SourceBlend = Blend.One;
            blendDesc.RenderTarget[0].DestinationBlend = Blend.InverseSourceColor;
            Screen = device.CreateBlendState(blendDesc);
            disposer.Collect(Screen);
        }

        public void Dispose()
        {
            disposer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
