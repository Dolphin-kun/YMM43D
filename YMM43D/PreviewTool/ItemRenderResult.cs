using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.PreviewTool
{
    internal readonly record struct ItemRenderResult(ID2D1Image? Image, DrawDescription Draw);
}
