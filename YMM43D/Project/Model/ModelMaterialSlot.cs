using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Model
{
    public class ModelMaterialSlot : Animatable
    {
        [Display(GroupName = "材質", Name = "材質名",
            Description = "モデルに書かれている材質の名前です。書き換えても変わりません")]
        [TextEditor]
        public string Name
        {
            get => name;
            set
            {
                if (name.Length == 0)
                    Set(ref name, value ?? string.Empty);
                else if (value != name)
                    OnPropertyChanged(nameof(Name));
            }
        }
        private string name = string.Empty;

        [Display(GroupName = "材質", Name = "テクスチャ",
            Description = "この材質に貼る画像。空ならモデルに書かれている画像を使います")]
        [FileSelector(FileGroupType.ImageItem)]
        public string Texture { get => texture; set => Set(ref texture, value ?? string.Empty); }
        private string texture = string.Empty;

        public ModelMaterialSlot()
        {
        }

        public ModelMaterialSlot(string name, string texture = "")
        {
            this.name = name;
            this.texture = texture;
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
