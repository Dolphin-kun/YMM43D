using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace BeamLight3D
{
    public class BeamLight3DPlugin : IShapePlugin
    {
        public string Name => "ビームライト3D";

        public bool IsExoShapeSupported => false;

        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
            => new BeamLight3DParameter(sharedData);
    }
}
