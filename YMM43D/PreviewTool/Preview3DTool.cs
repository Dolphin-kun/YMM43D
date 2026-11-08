using YMM43D.Commons;
using YMM43D.PreviewTool.ViewModels;
using YMM43D.PreviewTool.Views;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Update;

namespace YMM43D.PreviewTool
{
    public class Preview3DTool : IToolPlugin
    {
        public string Name => "3Dプレビュー";
        public Type ViewModelType => typeof(Preview3DViewModel);
        public Type ViewType => typeof(Preview3DView);
        public bool AllowMultipleInstances => true;

        public PluginDetailsAttribute Details
            => new() { AuthorName = "いるかぁぁ", ContentId = "nc504835" };

        public IPluginUpdater? Updater { get; } = new Ymm4InfoPluginUpdater<Preview3DTool>("1789299160303180");
    }
}
