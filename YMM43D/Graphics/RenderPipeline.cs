using Vortice.Direct3D11;
using Vortice.DXGI;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics
{
    /// <summary>
    /// 形状・シェーダー・入力レイアウト・定数バッファをひとまとめにした描画単位。
    /// </summary>
    /// <remarks>
    /// <para>
    /// このクラスの目的は、D3D11 の描画呼び出しに必ず付いて回る一連の定型処理
    /// （定数バッファ更新 → ステート設定 → シェーダー/バッファのバインド → Draw → 後始末）を
    /// 1箇所に閉じ込めることです。以前はこの手順がプラグインごとに書き写されていました。
    /// </para>
    /// <para>
    /// <typeparamref name="TConstants"/> は HLSL 側の <c>cbuffer</c> と同じレイアウトの構造体です。
    /// 定数バッファのサイズはこの型から決まるため、シェーダー側の宣言と食い違うと
    /// 描画結果が壊れます。
    /// </para>
    /// </remarks>
    /// <typeparam name="TConstants">スロット 0 に転送する定数バッファの型。</typeparam>
    public sealed class RenderPipeline<TConstants> : IDisposable where TConstants : unmanaged
    {
        private readonly DisposeCollector disposer = new();
        private readonly ID3D11InputLayout inputLayout;
        private readonly ID3D11Buffer constantBuffer;
        private readonly RenderStates states;

        /// <summary>描画する形状。このインスタンスが所有し、一緒に破棄します。</summary>
        public IMesh Mesh { get; }

        /// <summary>使用するシェーダー。このインスタンスが所有し、一緒に破棄します。</summary>
        public IMaterial Material { get; }

        /// <param name="device">リソースを生成するデバイス。</param>
        /// <param name="mesh">描画する形状。所有権はこのインスタンスに移ります。</param>
        /// <param name="material">使用するシェーダー。所有権はこのインスタンスに移ります。</param>
        /// <param name="states">
        /// 使用する描画ステート。省略すると <see cref="RenderStates.For"/> の共有インスタンスを使います。
        /// いずれの場合も所有権は移らず、このインスタンスは破棄しません。
        /// </param>
        public RenderPipeline(ID3D11Device device, IMesh mesh, IMaterial material, RenderStates? states = null)
        {
            Mesh = mesh;
            disposer.Collect(mesh);
            Material = material;
            disposer.Collect(material);
            this.states = states ?? RenderStates.For(device);

            inputLayout = device.CreateInputLayout(mesh.InputElements, material.VertexShaderBytecode);
            disposer.Collect(inputLayout);
            constantBuffer = D3D11Buffers.CreateConstantBuffer<TConstants>(device);
            disposer.Collect(constantBuffer);
        }

        /// <summary>
        /// 定数バッファを更新し、形状を1回描画します。
        /// </summary>
        /// <param name="context">描画先のデバイスコンテキスト。</param>
        /// <param name="constants">シェーダーに渡す定数。</param>
        /// <param name="settings">合成方法・カリングなど、この描画に固有の設定。</param>
        public void Draw(ID3D11DeviceContext context, in TConstants constants, in DrawSettings settings)
        {
            context.UpdateSubresource(in constants, constantBuffer);

            context.OMSetBlendState(states.GetBlend(settings.Blend));
            context.OMSetDepthStencilState(settings.IgnoreDepth ? states.DepthDisabled : states.DepthDefault);
            context.RSSetState(settings.Culling switch
            {
                FaceCulling.Back => states.CullBack,
                FaceCulling.Front => states.CullFront,
                _ => states.CullNone,
            });

            context.VSSetShader(Material.VertexShader);
            context.PSSetShader(Material.PixelShader);
            context.VSSetConstantBuffer(0, constantBuffer);
            context.PSSetConstantBuffer(0, constantBuffer);

            context.IASetInputLayout(inputLayout);
            context.IASetVertexBuffer(0, Mesh.VertexBuffer, Mesh.VertexStride, 0);
            context.IASetPrimitiveTopology(Mesh.Topology);

            if (settings.Texture is not null)
            {
                context.PSSetShaderResource(0, settings.Texture);
                context.PSSetSampler(0, settings.Sampler ?? states.LinearSampler);
            }

            if (Mesh.IndexBuffer is not null)
            {
                context.IASetIndexBuffer(Mesh.IndexBuffer, Format.R16_UInt, 0);
                context.DrawIndexed(Mesh.DrawCount, 0, 0);
            }
            else
            {
                context.Draw(Mesh.DrawCount, 0);
            }

            ResetState(context, settings);
        }

        /// <summary>
        /// 設定したステートを既定値に戻します。
        /// このコンテキストは YMM4 本体や他のプラグインの描画とも共有されるため、
        /// 自分が変更した状態を残さないようにします。
        /// </summary>
        private static void ResetState(ID3D11DeviceContext context, in DrawSettings settings)
        {
            // テクスチャを SRV に束ねたままにすると、同じテクスチャを次に
            // レンダーターゲットとして使うときに D3D11 が警告を出して結合を解除する。
            // D3D11 はスロットを空にする意味で null を受け付けるが、
            // Vortice の引数は null 非許容として宣言されている。
            if (settings.Texture is not null)
                context.PSSetShaderResource(0, null!);

            context.OMSetBlendState(null);
            context.OMSetDepthStencilState(null);
            context.RSSetState(null);
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}
