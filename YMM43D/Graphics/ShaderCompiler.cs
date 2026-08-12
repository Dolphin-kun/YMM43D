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
        /// <param name="profile">シェーダープロファイル（例: <c>vs_5_0</c>）。</param>
        /// <param name="sourceName">エラーメッセージに表示される名前。</param>
        /// <exception cref="InvalidOperationException">コンパイルに失敗した場合。</exception>
        /// <remarks>
        /// ソースは UTF-8 のバイト列にしてから渡します。文字列を直接渡すと ANSI に
        /// 変換されてしまい、日本語のコメントが壊れるためです。Shift-JIS では
        /// 「表」「ソ」「十」など 2 バイト目が 0x5C（バックスラッシュ）になる漢字があり、
        /// これが行継続と解釈されて次の行が丸ごとコメントに飲み込まれます。結果として
        /// 閉じ括弧が消え、「unexpected end of file」という無関係な箇所のエラーになります。
        /// UTF-8 なら後続バイトが必ず 0x80 以上になるため、この問題は起きません。
        /// </remarks>
        public static byte[] Compile(string source, string entryPoint, string profile, string sourceName = "")
        {
            var sourceBytes = Encoding.UTF8.GetBytes(source);
            var result = Compiler.Compile(sourceBytes, entryPoint, sourceName, profile, out var blob, out var errorBlob);
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
