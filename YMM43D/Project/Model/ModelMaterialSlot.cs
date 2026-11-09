using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YMM43D.Graphics.Models;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Settings;

namespace YMM43D.Project.Model
{
    public enum ModelSlotBlend
    {
        [Display(Name = "モデルのまま")]
        Model,

        [Display(Name = "そのまま")]
        Opaque,

        [Display(Name = "切り抜く")]
        Mask,

        [Display(Name = "透かす")]
        Blend,
    }

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
            Description = "この材質に貼る画像。空ならモデルのまま")]
        [FileSelector(FileGroupType.ImageItem)]
        public string Texture { get => texture; set => Set(ref texture, value ?? string.Empty); }
        private string texture = string.Empty;

        [Display(GroupName = "材質", Name = "透け方",
            Description = "画像の薄いところをどう扱うか")]
        [EnumComboBox]
        public ModelSlotBlend Blend
        {
            get => blend;
            set
            {
                Set(ref blend, value);
                OnPropertyChanged(nameof(IsMask));
            }
        }
        private ModelSlotBlend blend = ModelSlotBlend.Model;

        [Display(GroupName = "材質", Name = "切り抜きの境",
            Description = "これより薄いところを消します。0.5 くらいが目安")]
        [TextBoxSlider("F2", "", 0, 1)]
        [ShowPropertyEditorWhen(nameof(IsMask), true)]
        public double Cutoff
        {
            get => cutoff;
            set => Set(ref cutoff, Math.Clamp(value, 0, 1));
        }
        private double cutoff = 0.5;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsMask => Blend == ModelSlotBlend.Mask;

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
