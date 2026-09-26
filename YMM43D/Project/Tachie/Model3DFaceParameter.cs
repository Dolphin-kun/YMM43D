using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Tachie;

namespace YMM43D.Project.Tachie
{
    // 表情アイテムごとの設定。モデルに入っている表情の名前で選ぶ。
    public class Model3DFaceParameter : TachieFaceParameterBase
    {
        [Display(Name = "表情",
            Description = "モデルに入っている表情の名前（VRM なら Joy、Angry など）。空なら表情なし")]
        [TextEditor(AcceptsReturn = false)]
        public string Expression { get => expression; set => Set(ref expression, value ?? string.Empty); }
        private string expression = string.Empty;

        [Display(Name = "強さ")]
        [TextBoxSlider("F0", "%", 0, 100)]
        public double Strength { get => strength; set => Set(ref strength, Math.Clamp(value, 0, 100)); }
        private double strength = 100;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
