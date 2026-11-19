using System.ComponentModel.DataAnnotations;
using YMM43D.Graphics.Models;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Model
{
    public class ModelMaterialSlot : Animatable
    {
        private const string Group = "材質";

        [Display(GroupName = Group, Name = "材質名", Description = "モデルに書かれている材質の名前で、書き換えても変わりません")]
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

        [Display(GroupName = Group, Name = "テクスチャ", Description = "この材質に貼る画像で、空ならモデルのままです")]
        [FileSelector(FileGroupType.ImageItem)]
        public string Texture { get => texture; set => Set(ref texture, value ?? string.Empty); }
        private string texture = string.Empty;

        [Display(GroupName = Group, Name = "透け方", Description = "画像の薄いところをどう扱うか")]
        [EnumComboBox]
        public ModelSlotBlend Blend { get; set => Set(ref field, value, nameof(Blend), nameof(IsMask)); } = ModelSlotBlend.Model;

        public bool IsMask => Blend == ModelSlotBlend.Mask;

        [Display(GroupName = Group, Name = "切り抜きの境", Description = "これより薄いところを消し、0.5 くらいが目安です")]
        [TextBoxSlider("F2", "", 0, 1)]
        [ShowPropertyEditorWhen(nameof(IsMask), true)]
        public double Cutoff { get; set => Set(ref field, Math.Clamp(value, 0, 1)); } = 0.5;

        internal (ModelBlend Blend, float Cutoff)? Override => Blend switch
        {
            ModelSlotBlend.Opaque => (ModelBlend.Opaque, 0f),
            ModelSlotBlend.Mask => (ModelBlend.Mask, (float)Cutoff),
            ModelSlotBlend.Blend => (ModelBlend.Blend, 0f),
            _ => null,
        };

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
