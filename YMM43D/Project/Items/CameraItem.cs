using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Project.Items
{
    public sealed class CameraItem : BaseItem, ISceneCamera
    {
        private const string Place = "カメラ位置";

        private const int FirstOrder = 100;

        [Display(GroupName = Place, Name = "X", Description = "カメラを置く位置。右が正", Order = FirstOrder)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        public Animation X { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "Y", Description = "下が正。画面の座標と同じ向き", Order = FirstOrder + 1)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        public Animation Y { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "Z", Description = "手前が正。0 の面が、YMM4 が 2D で描く面", Order = FirstOrder + 2)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        public Animation Z { get; } = new(DefaultZ, -1000000, 1000000);

        [Display(GroupName = Place, Name = "向きの決め方",
            Description = "回転角で向けるか、決めた点を向き続けるか", Order = FirstOrder + 3)]
        [EnumComboBox]
        public CameraAim AimMode
        {
            get => aimMode;
            set => Set(ref aimMode, value);
        }
        private CameraAim aimMode = CameraAim.Rotation;

        [Display(GroupName = Place, Name = "水平回転", Description = "横を向きます", Order = FirstOrder + 4)]
        [AnimationSlider("F1", "°", -180, 180)]
        [ShowPropertyEditorWhen(nameof(AimMode), CameraAim.Rotation)]
        public Animation Yaw { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "垂直回転", Description = "上下を向きます", Order = FirstOrder + 5)]
        [AnimationSlider("F1", "°", -CameraState.MaxPitch, CameraState.MaxPitch)]
        [ShowPropertyEditorWhen(nameof(AimMode), CameraAim.Rotation)]
        public Animation Pitch { get; } = new(0, -CameraState.MaxPitch, CameraState.MaxPitch);

        [Display(GroupName = Place, Name = "注目X", Description = "見つめる点。右が正", Order = FirstOrder + 6)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(AimMode), CameraAim.Target)]
        public Animation TargetX { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "注目Y", Description = "下が正。画面の座標と同じ向き", Order = FirstOrder + 7)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(AimMode), CameraAim.Target)]
        public Animation TargetY { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "注目Z", Description = "手前が正。0 の面が、YMM4 が 2D で描く面", Order = FirstOrder + 8)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(AimMode), CameraAim.Target)]
        public Animation TargetZ { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "傾き", Description = "視線を軸にして画面を回します", Order = FirstOrder + 9)]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Roll { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "視野角変更",
            Description = "切ると、Z=0 の面が YMM4 の画面とちょうど同じ大きさに写る画角になります",
            Order = FirstOrder + 10)]
        [ToggleSlider]
        public bool IsFieldOfViewEnabled
        {
            get => isFieldOfViewEnabled;
            set => Set(ref isFieldOfViewEnabled, value);
        }
        private bool isFieldOfViewEnabled;

        [Display(GroupName = Place, Name = "視野角", Description = "縦方向の画角。狭いほど望遠になります",
            Order = FirstOrder + 11)]
        [AnimationSlider("F1", "°", 1, 179)]
        [ShowPropertyEditorWhen(nameof(IsFieldOfViewEnabled), true)]
        public Animation FieldOfView { get; } = new(DefaultFieldOfView, 1, 179);

        public override string Label => "3Dカメラ";

        public override Color ItemColor
        {
            get => itemColor;
            set => Set(ref itemColor, value);
        }
        private Color itemColor = Color.FromRgb(0xFF, 0x99, 0x00);

        public override TimeSpan OriginalContentLength => TimeSpan.Zero;

        public override TimeSpan ContentLength => TimeSpan.Zero;

        private const double DefaultZ = SceneProjection.DefaultFocalDistance * WorldScale.PixelsPerUnit;

        private const double DefaultFieldOfView = 57;

        public CameraState GetState(in FrameContext itemTime)
        {
            var position = ToWorld(X, Y, Z, itemTime);
            var (yaw, pitch) = AimMode == CameraAim.Target
                ? GetAngles(position, ToWorld(TargetX, TargetY, TargetZ, itemTime))
                : (Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime));

            return new CameraState(
                position,
                yaw,
                pitch,
                Roll.GetFloat(itemTime),
                IsFieldOfViewEnabled ? FieldOfView.GetFloat(itemTime) : 0f);
        }

        private static Vector3 ToWorld(Animation x, Animation y, Animation z, in FrameContext itemTime)
            => new(
                WorldScale.ToWorld(x.GetFloat(itemTime)),
                -WorldScale.ToWorld(y.GetFloat(itemTime)),
                WorldScale.ToWorld(z.GetFloat(itemTime)));

        private static (float Yaw, float Pitch) GetAngles(in Vector3 from, in Vector3 to)
        {
            var direction = to - from;

            if (direction.LengthSquared() < 1e-8f)
                return (0f, 0f);

            direction = Vector3.Normalize(direction);

            var pitch = Rotation3D.ToDegrees(MathF.Asin(Math.Clamp(direction.Y, -1f, 1f)));
            var yaw = Rotation3D.ToDegrees(MathF.Atan2(-direction.X, -direction.Z));

            return (yaw, Math.Clamp(pitch, -CameraState.MaxPitch, CameraState.MaxPitch));
        }

        public void Move(in CameraMove move, in EditScope scope)
        {
            scope.Nudge(Roll, move.Roll);
            scope.Nudge(X, WorldScale.ToPixels(move.Shift.X));
            scope.Nudge(Y, -WorldScale.ToPixels(move.Shift.Y));
            scope.Nudge(Z, WorldScale.ToPixels(move.Shift.Z));

            if (AimMode == CameraAim.Target)
                return;

            scope.Nudge(Yaw, move.Yaw);
            scope.Nudge(Pitch, move.Pitch);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [X, Y, Z, Yaw, Pitch, TargetX, TargetY, TargetZ, Roll, FieldOfView];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription)
            => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }

    public enum CameraAim
    {
        [Display(Name = "回転で指定", Description = "水平・垂直の回転角で向きを決めます")]
        Rotation,

        [Display(Name = "注目点で指定", Description = "決めた点を向き続けます。被写体を追うのに向きます")]
        Target,
    }
}
