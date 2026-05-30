using YMM43D.Preview.ViewModels;
using YMM43D.Preview.Views;
using YukkuriMovieMaker.Plugin;

namespace YMM43D.Preview
{
    public class Preview3DSettingsTool : IToolPlugin
    {
        public string Name => "3Dプレビュー設定";

        public Type ViewModelType => typeof(Preview3DSettingsToolViewModel);
        public Type ViewType => typeof(Preview3DSettingsToolView);

        public bool AllowMultipleInstances => false;
    }
}
