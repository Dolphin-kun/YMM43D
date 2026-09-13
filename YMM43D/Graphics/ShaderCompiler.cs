using System.Text;
using Vortice.D3DCompiler;

namespace YMM43D.Graphics
{
    public static class ShaderCompiler
    {
        public static byte[] Compile(string source, string entryPoint, string profile, string sourceName = "")
        {
            var result = Compiler.Compile(
                Encoding.UTF8.GetBytes(source), entryPoint, sourceName, profile, out var blob, out var errorBlob);

            try
            {
                if (result.Failure)
                {
                    throw new InvalidOperationException(
                        $"シェーダーのコンパイルに失敗しました [{profile} {entryPoint}]: {errorBlob?.AsString() ?? "(エラー情報なし)"}");
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
