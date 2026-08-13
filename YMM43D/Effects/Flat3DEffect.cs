using System.ComponentModel.DataAnnotations;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM43D.Effects
{
    [VideoEffect("3D空間に置く", ["3D"], [])]
    public class Flat3DEffect : VideoEffect3DBase
    {
        private const string Group = "3D空間に置く";

        public override string Label => "3D空間に置く";

        [Display(GroupName = Group, Name = "回転X", Description = "横軸まわり。板を奥や手前に倒します", Order = 0)]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "回転Y", Description = "縦軸まわり。板を左右に振ります", Order = 1)]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "回転Z", Description = "画面の中で回します。アイテムの「回転」と同じ向き", Order = 2)]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "陰影をつけない",
            Description = "光源を無視して、元の絵のまま置きます", Order = 3)]
        [ToggleSlider]
        public bool IsUnlit { get => isUnlit; set => Set(ref isUnlit, value); }
        private bool isUnlit;

        [Display(GroupName = Group, Name = "他のものを隠す",
            Description = "入れると板が奥行きを持ち、後ろにあるものを隠します。"
                + "同じ面に並べたり半透明にしたりすると、境目がちらつくことがあります",
            Order = 4)]
        [ToggleSlider]
        public bool WritesDepth
        {
            get => writesDepth;
            set => Set(ref writesDepth, value);
        }
        private bool writesDepth;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
            => AttachProcessor(new Flat3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [RotationX, RotationY, RotationZ, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
