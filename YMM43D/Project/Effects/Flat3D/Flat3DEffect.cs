using System.ComponentModel;
using System.Numerics;
using System.ComponentModel.DataAnnotations;
using YMM43D.Commons;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM43D.Project.Effects.Flat3D
{
    [VideoEffect("3D空間に置く", ["3D"], [])]
    public class Flat3DEffect : VideoEffect3DBase, ISceneRotation3D
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

        Matrix4x4 ISceneRotation3D.ToOrientation(float x, float y, float z)
            => Rotation3D.ForObject(-x, -y, -z);

        Vector3 ISceneRotation3D.ToAngles(in Matrix4x4 orientation)
            => -Rotation3D.ToAngles(orientation);

        [Display(GroupName = Group, Name = "陰影", Description = "光源にしたがって陰影をつけます。切ると、元の絵のまま置きます", Order = 3)]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsUnlit
        {
            get => isUnlit;
            set
            {
                Set(ref isUnlit, value);
                OnPropertyChanged(nameof(IsLit));
            }
        }
        private bool isUnlit;

        [Display(GroupName = Group, Name = "つや",
            Description = "光が映り込んだ明るい点の強さ", Order = 5)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Group, Name = "つやの鋭さ",
            Description = "大きいほど映り込みが小さく締まり、磨いたように見えます", Order = 6)]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        internal SurfaceGloss GetGloss(in FrameContext time)
            => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        [Display(GroupName = Group, Name = "他のものを隠す",
            Description = "入れると板が奥行きを持ち、後ろにあるものを隠します"
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
            => [RotationX, RotationY, RotationZ, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(
            int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
