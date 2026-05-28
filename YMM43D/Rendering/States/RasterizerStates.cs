using System;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Rendering.States
{
    public class RasterizerStates : IDisposable
    {
        private readonly DisposeCollector disposer = new();

        /// <summary>カリングなし（両面描画）</summary>
        public ID3D11RasterizerState CullNone { get; }
        
        /// <summary>背面カリング（前面のみ描画）</summary>
        public ID3D11RasterizerState CullBack { get; }

        /// <summary>前面カリング（背面のみ描画）</summary>
        public ID3D11RasterizerState CullFront { get; }

        public RasterizerStates(ID3D11Device device)
        {
            var desc = new RasterizerDescription
            {
                FillMode = FillMode.Solid,
                DepthClipEnable = true,
                MultisampleEnable = true,
                AntialiasedLineEnable = true,
                ScissorEnable = false,
                CullMode = CullMode.None
            };
            CullNone = device.CreateRasterizerState(desc);
            disposer.Collect(CullNone);

            desc.CullMode = CullMode.Back;
            CullBack = device.CreateRasterizerState(desc);
            disposer.Collect(CullBack);

            desc.CullMode = CullMode.Front;
            CullFront = device.CreateRasterizerState(desc);
            disposer.Collect(CullFront);
        }

        public void Dispose()
        {
            disposer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
