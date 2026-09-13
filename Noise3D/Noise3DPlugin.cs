using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Noise3D
{
    public class Noise3DPlugin : IShapePlugin
    {
        public string Name => "ノイズ3D";

        public bool IsExoShapeSupported => false;

        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
            => new Noise3DParameter(sharedData);
    }
}
