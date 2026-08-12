using Vortice.Direct2D1;
using YMM43D.Integration;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace Camera3D
{
    /// <summary>
    /// カメラアイテムの見た目。何も描きません。
    /// </summary>
    /// <remarks>
    /// カメラは撮る側なので、映像には現れません。ただし <c>Output</c> を空のままには
    /// できないので、中身の無い画像を返します。3Dプレビューにはガイドとして
    /// カメラの形が出ます。
    /// </remarks>
    internal sealed class Camera3DSource(IGraphicsDevicesAndContext devices) : IShapeSource
    {
        private readonly Renderer3DTo2D renderer = new();
        private ID2D1Image? output;

        public ID2D1Image Output => output ?? throw new InvalidOperationException(
            "まだ画像が生成されていません。Update を先に呼んでください。");

        public void Update(TimelineItemSourceDescription description)
            => output = renderer.RenderEmpty(devices);

        public void Dispose()
        {
            renderer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
