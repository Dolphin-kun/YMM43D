using System.ComponentModel.DataAnnotations;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Plugin.Tachie;

namespace YMM43D.Project.Tachie
{
    public class Model3DFaceParameter : TachieFaceParameterBase
    {
        private const string Group = "3Dモデル";

        [Display(GroupName = Group, Name = "表情", Description = "モデルに入っている表情（VRM なら Joy、Angry など）")]
        [ExpressionSelector]
        public string Expression { get; set => Set(ref field, value ?? string.Empty); } = string.Empty;

        [Display(GroupName = Group, Name = "強さ", Description = "その表情をどれだけ強く出すか")]
        [TextBoxSlider("F0", "%", 0, 100)]
        public double Strength { get; set => Set(ref field, Math.Clamp(value, 0, 100)); } = 100;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
