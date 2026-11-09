using System.ComponentModel.DataAnnotations;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace YMM43D.Project.Model
{
    public class ModelExpressionValue : Animatable
    {
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

        [Display(Name = "強さ", Description = "その表情をどれだけ強く出すか")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Weight { get; } = new(0, 0, 100);

        public ModelExpressionValue()
        {
        }

        public ModelExpressionValue(string name) => this.name = name;

        internal bool IsStill(in FrameContext time) => Weight.GetFloat(time) == 0f;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Weight];
    }
}
