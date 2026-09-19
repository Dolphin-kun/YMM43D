using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    internal static class SourceKey
    {
        // YMM4 はアイテムごとに別の IGraphicsDevicesAndContext を渡すが、
        // 同じ描画系統の中では D3D を共有するため、これを系統の目印にする。
        public static object? Of(IGraphicsDevicesAndContext? devices) => devices?.D3D;
    }
}
