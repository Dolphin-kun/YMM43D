using System;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Preview.ViewModels
{
    public class Preview3DSettingsViewModel : Bindable
    {
        private SceneCamera? camera;
        public SceneCamera? Camera
        {
            get => camera;
            set
            {
                if (Set(ref camera, value))
                    OnPropertyChanged(nameof(IsActive));
            }
        }

        public bool IsActive => Camera != null;

        public Preview3DSettingsViewModel()
        {
        }
    }
}
