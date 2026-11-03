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

        [Display(Name = "強さ", Description = "0 でなし、100 でその表情いっぱい")]
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
