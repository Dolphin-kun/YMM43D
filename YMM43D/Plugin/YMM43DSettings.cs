using YukkuriMovieMaker.Plugin;

namespace YMM43D.Plugin
{
    internal sealed class YMM43DSettings : SettingsBase<YMM43DSettings>
    {
        public override SettingsCategory Category => SettingsCategory.Tool;

        public override string Name => "YMM43D";

        public override bool HasSettingView => true;

        public override object? SettingView => new YMM43DSettingsView { DataContext = this };

        public bool UsesDoubleBuffer { get; set => Set(ref field, value); } = true;

        public override void Initialize()
        {
        }
    }
}
