using System.Text;
using Vortice.D3DCompiler;

namespace YMM43D.Graphics
{
    public static class ShaderCompiler
    {
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
