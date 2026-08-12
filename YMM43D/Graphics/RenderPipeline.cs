using Vortice.Direct3D11;
using Vortice.DXGI;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics
{
    /// <summary>
    /// 形状・シェーダー・入力レイアウト・定数バッファをひとまとめにした描画単位。
    /// </summary>
    /// <remarks>
    /// 描画呼び出しに付いて回る定型処理（定数バッファ更新 → ステート設定 → バインド →
    /// Draw → 後始末）を1箇所に閉じ込めます。
    /// <para>
    /// <typeparamref name="TConstants"/> は HLSL 側の <c>cbuffer</c> と同じレイアウトの構造体です。
    /// 定数バッファのサイズはこの型から決まるため、宣言が食い違うと描画結果が壊れます。
    /// </para>
    /// </remarks>
    /// <typeparam name="TConstants">スロット 0 に転送する定数バッファの型。</typeparam>
    public sealed class RenderPipeline<TConstants> : IDisposable where TConstants : unmanaged
    {
        private readonly DisposeCollector disposer = new();
        private readonly ID3D11InputLayout inputLayout;
        private readonly ID3D11Buffer constantBuffer;
        private readonly RenderStates states;

        /// <summary>
        /// 既定で描画する形状。このインスタンスが所有し、一緒に破棄します。
        /// 形状を都度渡す使い方では <c>null</c> です。
        /// </summary>
        public IMesh? Mesh { get; }

        /// <summary>使用するシェーダー。このインスタンスが所有し、一緒に破棄します。</summary>
        public IMaterial Material { get; }

        /// <param name="mesh">描画する形状。所有権はこのインスタンスに移ります。</param>
        /// <param name="material">使用するシェーダー。所有権はこのインスタンスに移ります。</param>
        /// <param name="states">
        /// 使用する描画ステート。省略すると <see cref="RenderStates.For"/> の共有インスタンスを使います。
        /// いずれの場合も所有権は移らず、このインスタンスは破棄しません。
        /// </param>
        public RenderPipeline(ID3D11Device device, IMesh mesh, IMaterial material, RenderStates? states = null)
            : this(device, mesh.InputElements, material, states)
        {
            Mesh = mesh;
            disposer.Collect(mesh);
        }

        /// <summary>
        /// 形状を固定せず、描画のたびに渡す形で作ります。
        /// </summary>
        /// <remarks>
        /// 同じシェーダーで頂点の並びだけが違う形状を描き分けるときに使います。
        /// 入力レイアウトはここで固定されるので、渡す形状の
        /// <see cref="IMesh.InputElements"/> はすべて <paramref name="inputElements"/> と
        /// 同じである必要があります。形状の寿命は呼び出し側が持ちます。
        /// </remarks>
        /// <param name="inputElements">描画する形状に共通の入力レイアウト。</param>
        public RenderPipeline(
            ID3D11Device device,
            InputElementDescription[] inputElements,
            IMaterial material,
            RenderStates? states = null)
        {
            Material = material;
            disposer.Collect(material);
            this.states = states ?? RenderStates.For(device);

            inputLayout = device.CreateInputLayout(inputElements, material.VertexShaderBytecode);
            disposer.Collect(inputLayout);
            constantBuffer = D3D11Buffers.CreateConstantBuffer<TConstants>(device);
            disposer.Collect(constantBuffer);
        }

        /// <summary>
        /// 定数バッファを更新し、形状を1回描画します。
        /// </summary>
        /// <param name="mesh">
        /// 描画する形状。省略すると <see cref="Mesh"/> を使います。
        /// 入力レイアウトが生成時のものと同じ形状に限ります。
        /// </param>
        public void Draw(
            ID3D11DeviceContext context,
            in TConstants constants,
            in DrawSettings settings,
            IMesh? mesh = null)
        {
            mesh ??= Mesh ?? throw new InvalidOperationException(
                "形状を持たないパイプラインです。描画する形状を渡してください。");

            context.UpdateSubresource(in constants, constantBuffer);

            context.OMSetBlendState(
                settings.DepthOnly ? states.NoColorWrite : states.GetBlend(settings.Blend));

            context.OMSetDepthStencilState(
                settings.IgnoreDepth && !settings.DepthOnly ? states.DepthDisabled
                : settings.SkipDepthWrite && !settings.DepthOnly ? states.DepthTestOnly
                : states.DepthDefault);
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
            context.IASetVertexBuffer(0, mesh.VertexBuffer, mesh.VertexStride, 0);
            context.IASetPrimitiveTopology(mesh.Topology);

            if (settings.Texture is not null)
            {
                context.PSSetShaderResource(0, settings.Texture);
                context.PSSetSampler(0, settings.Sampler ?? states.LinearSampler);
            }

            if (mesh.IndexBuffer is not null)
            {
                context.IASetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat, 0);
                context.DrawIndexed(mesh.DrawCount, 0, 0);
            }
            else
            {
                context.Draw(mesh.DrawCount, 0);
            }

            ResetState(context, settings);
        }

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
