using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM43D.Project.Shape
{
    public class Shape3DPlugin : IShapePlugin
    {
        public string Name => "3Dアイテム";

        public bool IsExoShapeSupported => false;

        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
            => new Shape3DParameter(sharedData);
    }
}
