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
    [VideoEffect("3D空間に置く", ["3D"], ["3D空間", "置く", "配置", "奥行き", "板", "3D化", "立体空間", "3D space", "place", "plane", "depth"])]
    public class Flat3DEffect : VideoEffect3DBase, ISceneRotation3D
    {
        private const string Group = "3D空間に置く";

        public override string Label => "3D空間に置く";

        [Display(GroupName = Group, Name = "回転X", Description = "横軸まわりの回転で、板を奥や手前に倒します")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "回転Y", Description = "縦軸まわりの回転で、板を左右に振ります")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "回転Z", Description = "画面の中での回転で、アイテムの「回転」と同じ向きです")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Group, Name = "陰影", Description = "光源にしたがって陰影をつけます")]
        [ToggleSlider]
        public bool IsLit { get => !IsUnlit; set => IsUnlit = !value; }

        public bool IsUnlit { get; set => Set(ref field, value, nameof(IsUnlit), nameof(IsLit)); }

        [Display(GroupName = Group, Name = "他のものを隠す", Description = "板に奥行きを持たせ、後ろにあるものを隠します")]
        [ToggleSlider]
        public bool WritesDepth { get; set => Set(ref field, value); }

        [Display(GroupName = Group, Name = "つや", Description = "光が映り込んだ明るい点の強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation Gloss { get; } = new(0, 0, 1000);

        [Display(GroupName = Group, Name = "つやの鋭さ", Description = "映り込んだ明るい点の締まり具合")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(IsUnlit), false)]
        public Animation GlossSharpness { get; } = new(50, 0, 100);

        Matrix4x4 ISceneRotation3D.ToOrientation(float x, float y, float z) => Rotation3D.ForObject(-x, -y, -z);

        Vector3 ISceneRotation3D.ToAngles(in Matrix4x4 orientation) => -Rotation3D.ToAngles(orientation);

        internal SurfaceGloss GetGloss(in FrameContext time)
            => IsUnlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(Gloss.GetFloat(time), GlossSharpness.GetFloat(time));

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => AttachProcessor(new Flat3DProcessor(this, devices));

        protected override IEnumerable<IAnimatable> GetAnimatables() => [RotationX, RotationY, RotationZ, Gloss, GlossSharpness, CameraSyncAnimation];

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }
}
