using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    internal static class SourceKey
    {
        public static object? Of(IGraphicsDevicesAndContext? devices) => devices?.D3D;
    }
}
