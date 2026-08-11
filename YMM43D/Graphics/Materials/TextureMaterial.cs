using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Graphics.Materials
{
    /// <summary>
    /// スロット 0 のテクスチャを頂点カラーと乗算して出力するシェーダー。
    /// YMM4 のアイテム画像を 3D 空間に貼り付けるのに使います。
    /// 定数バッファは <see cref="TransformConstants"/> です。
    /// </summary>
    public sealed class TextureMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        public TextureMaterial(ID3D11Device device)
        {
            var source = ShaderSource.StandardPrologue + """
                Texture2D    tex  : register(t0);
                SamplerState samp : register(s0);

                float4 PSMain(PS_IN input) : SV_TARGET
                {
                    float4 col = input.Col * tex.Sample(samp, input.Tex);
                    col.a *= Opacity;
                    return col;
                }
                """;

            VertexShaderBytecode = ShaderCompiler.Compile(source, "VSMain", "vs_5_0", nameof(TextureMaterial));
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            var pixelShaderBytecode = ShaderCompiler.Compile(source, "PSMain", "ps_5_0", nameof(TextureMaterial));
            PixelShader = device.CreatePixelShader(pixelShaderBytecode);
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
