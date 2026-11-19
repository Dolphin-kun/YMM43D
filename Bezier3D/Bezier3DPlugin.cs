using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Bezier3D
{
    public class Bezier3DPlugin : IShapePlugin
    {
        public string Name => "3Dベジェ";

        public bool IsExoShapeSupported => false;

        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData) => new Bezier3DParameter(sharedData);

        public PluginDetailsAttribute Details => new() { AuthorName = "いるかぁぁ", ContentId = "nc504835" };
    }
}
