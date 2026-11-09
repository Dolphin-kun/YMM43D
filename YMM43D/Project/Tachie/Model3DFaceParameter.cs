using System.ComponentModel.DataAnnotations;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Tachie;

namespace YMM43D.Project.Tachie
{
    public class Model3DFaceParameter : TachieFaceParameterBase
    {
        [Display(Name = "表情",
            Description = "モデルに入っている表情（VRM なら Joy、Angry など）")]
        [ExpressionSelector]
        public string Expression { get => expression; set => Set(ref expression, value ?? string.Empty); }
        private string expression = string.Empty;

        [Display(Name = "強さ")]
        [TextBoxSlider("F0", "%", 0, 100)]
        public double Strength { get => strength; set => Set(ref strength, Math.Clamp(value, 0, 100)); }
        private double strength = 100;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
