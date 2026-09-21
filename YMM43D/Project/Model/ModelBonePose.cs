using System.ComponentModel.DataAnnotations;
using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace YMM43D.Project.Model
{
    // 骨 1 本の姿勢。モデルに書かれている骨の名前で結びつける。
    public class ModelBonePose : Animatable
    {
        // 名前は折りたたみの見出しに出すので、編集欄は作らない。
        internal const string Group = "骨";

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

        [Display(GroupName = Group, Name = "回転X", Description = "骨そのものを軸にして回します")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "回転Y")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "回転Z")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        public ModelBonePose()
        {
        }

        public ModelBonePose(string name) => this.name = name;

        // 骨から見た回転。親から見た位置関係に、この回転を先に掛ける。
        internal Matrix4x4 GetRotation(in FrameContext time)
            => Rotation3D.ForObject(
                RotationX.GetFloat(time), RotationY.GetFloat(time), RotationZ.GetFloat(time));

        internal bool IsStill(in FrameContext time)
            => RotationX.GetFloat(time) == 0f
            && RotationY.GetFloat(time) == 0f
            && RotationZ.GetFloat(time) == 0f;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [RotationX, RotationY, RotationZ];
    }
}
