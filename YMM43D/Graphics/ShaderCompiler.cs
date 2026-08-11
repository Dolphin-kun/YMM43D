using System.Text;
using Vortice.D3DCompiler;

namespace YMM43D.Graphics
{
    /// <summary>
    /// HLSL のコンパイルヘルパー。
    /// </summary>
    public static class ShaderCompiler
    {
        /// <summary>
        /// HLSL ソースをコンパイルしてバイトコードを返します。
        /// </summary>
        /// <param name="source">HLSL ソースコード。</param>
        /// <param name="entryPoint">エントリポイント関数名。</param>
        /// <param name="profile">シェーダープロファイル（例: <c>vs_5_0</c>）。</param>
        /// <param name="sourceName">エラーメッセージに表示される名前。</param>
        /// <exception cref="InvalidOperationException">コンパイルに失敗した場合。</exception>
        public static byte[] Compile(string source, string entryPoint, string profile, string sourceName = "")
        {
            var result = Compiler.Compile(source, entryPoint, sourceName, profile, out var blob, out var errorBlob);
            try
            {
                if (result.Failure)
                {
                    var error = errorBlob is not null
                        ? Encoding.UTF8.GetString(errorBlob.AsBytes())
                        : "(エラー情報なし)";
                    throw new InvalidOperationException(
                        $"シェーダーのコンパイルに失敗しました [{profile} {entryPoint}]: {error}");
                }

                return blob!.AsBytes();
            }
            finally
            {
                errorBlob?.Dispose();
                blob?.Dispose();
            }
        }
    }
}
