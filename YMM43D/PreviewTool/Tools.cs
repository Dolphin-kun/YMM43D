using YMM43D.PreviewTool.ViewModels;
using YMM43D.PreviewTool.Views;
using YukkuriMovieMaker.Plugin;

namespace YMM43D.PreviewTool
{
    public class Preview3DTool : IToolPlugin
    {
        public string Name => "3Dプレビュー";
        public Type ViewModelType => typeof(Preview3DViewModel);
        public Type ViewType => typeof(Preview3DView);
        public bool AllowMultipleInstances => true;
    }
}
