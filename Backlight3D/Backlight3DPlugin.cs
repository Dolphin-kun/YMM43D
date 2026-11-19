using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Backlight3D
{
    public class Backlight3DPlugin : IShapePlugin
    {
        public string Name => "逆光3D";

        public bool IsExoShapeSupported => false;

        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
            => new Backlight3DParameter(sharedData);

        public PluginDetailsAttribute Details
            => new() { AuthorName = "いるかぁぁ", ContentId = "nc504835" };
    }
}
