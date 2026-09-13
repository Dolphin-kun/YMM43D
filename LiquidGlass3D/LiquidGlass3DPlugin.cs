using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace LiquidGlass3D
{
    public class LiquidGlass3DPlugin : IShapePlugin
    {
        public string Name => "リキッドグラス3D";

        public bool IsExoShapeSupported => false;

        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
            => new LiquidGlass3DParameter(sharedData);
    }
}
