using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM43D.Project.Model
{
    public class Model3DPlugin : IShapePlugin
    {
        public string Name => "3Dモデル";

        public bool IsExoShapeSupported => false;

        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
            => new Model3DParameter(sharedData);
    }
}
