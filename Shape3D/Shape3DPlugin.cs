using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Shape3D
{
    public class Shape3DPlugin: IShapePlugin
    {
        public string Name => "3Dアイテム";
        public bool IsExoShapeSupported => false;
        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
        {
            return new Shape3DParameter(sharedData);
        }
    }
}
