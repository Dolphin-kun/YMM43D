using System.ComponentModel;
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
    public sealed class CameraItem : BaseItem, ISceneCamera, ISceneMarkerSource
    {
        private const string Place = "カメラ位置";

        private const int FirstOrder = 100;

        [Display(GroupName = Place, Name = "置き方",
            Description = "カメラの位置と向きの決め方", Order = FirstOrder)]
        [EnumComboBox]
        public CameraAim AimMode
        {
            get => aimMode;
            set
            {
                Set(ref aimMode, value);
                OnPropertyChanged(nameof(AimsByRotation));
                OnPropertyChanged(nameof(AimsAtTarget));
                OnPropertyChanged(nameof(HasPosition));
                OnPropertyChanged(nameof(HasTarget));
                OnPropertyChanged(nameof(HasAngles));
            }
        }
        private CameraAim aimMode = CameraAim.Target;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool AimsByRotation => AimMode == CameraAim.Rotation;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool AimsAtTarget => AimMode == CameraAim.Target;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool AimsByOrbit => AimMode == CameraAim.Orbit;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool HasPosition => AimMode != CameraAim.Orbit;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool HasTarget => AimMode != CameraAim.Rotation;

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool HasAngles => AimMode != CameraAim.Target;

        [Display(GroupName = Place, Name = "X", Description = "カメラを置く位置。右が正", Order = FirstOrder + 1)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(HasPosition), true)]
        public Animation X { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "Y", Description = "下が正。画面の座標と同じ向き", Order = FirstOrder + 2)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(HasPosition), true)]
        public Animation Y { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "Z", Description = "手前が正。0 の面が、YMM4 が 2D で描く面", Order = FirstOrder + 3)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(HasPosition), true)]
        public Animation Z { get; } = new(DefaultZ, -1000000, 1000000);

        [Display(GroupName = Place, Name = "注目X", Description = "見つめる点。右が正", Order = FirstOrder + 4)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(HasTarget), true)]
        public Animation TargetX { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "注目Y", Description = "下が正。画面の座標と同じ向き", Order = FirstOrder + 5)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(HasTarget), true)]
        public Animation TargetY { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "注目Z", Description = "手前が正。0 の面が、YMM4 が 2D で描く面", Order = FirstOrder + 6)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(HasTarget), true)]
        public Animation TargetZ { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "距離",
            Description = "注目点からどれだけ離れて回り込むか", Order = FirstOrder + 7)]
        [AnimationSlider("F1", "px", 100, 5000)]
        [ShowPropertyEditorWhen(nameof(AimsByOrbit), true)]
        public Animation Distance { get; } = new(DefaultZ, 1, 1000000);

        [Display(GroupName = Place, Name = "水平回転", Description = "横を向きます", Order = FirstOrder + 8)]
        [AnimationSlider("F1", "°", -180, 180)]
        [ShowPropertyEditorWhen(nameof(HasAngles), true)]
        public Animation Yaw { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "垂直回転", Description = "上下を向きます", Order = FirstOrder + 9)]
        [AnimationSlider("F1", "°", -CameraState.MaxPitch, CameraState.MaxPitch)]
        [ShowPropertyEditorWhen(nameof(HasAngles), true)]
        public Animation Pitch { get; } = new(0, -CameraState.MaxPitch, CameraState.MaxPitch);

        [Display(GroupName = Place, Name = "傾き", Description = "視線を軸にして画面を回します", Order = FirstOrder + 10)]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Roll { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "視野角変更",
            Description = "切ると、Z=0 の面が YMM4 の画面とちょうど同じ大きさに写る画角になります",
            Order = FirstOrder + 11)]
        [ToggleSlider]
        public bool IsFieldOfViewEnabled
        {
            get => isFieldOfViewEnabled;
            set => Set(ref isFieldOfViewEnabled, value);
        }
        private bool isFieldOfViewEnabled;

        [Display(GroupName = Place, Name = "視野角", Description = "縦方向の画角。狭いほど望遠になります",
            Order = FirstOrder + 12)]
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
            var roll = Roll.GetFloat(itemTime);
            var fieldOfView = IsFieldOfViewEnabled ? FieldOfView.GetFloat(itemTime) : 0f;

            if (AimMode == CameraAim.Orbit)
            {
                var aimed = new CameraState(
                    Vector3.Zero,
                    Yaw.GetFloat(itemTime),
                    Math.Clamp(Pitch.GetFloat(itemTime), -CameraState.MaxPitch, CameraState.MaxPitch),
                    roll,
                    fieldOfView);

                // 注目点から、見ている向きの反対側へ距離のぶん下がった場所に置く。
                return aimed with
                {
                    Position = ToWorld(TargetX, TargetY, TargetZ, itemTime)
                             - aimed.Forward * WorldScale.ToWorld(Distance.GetFloat(itemTime)),
                };
            }

            var position = ToWorld(X, Y, Z, itemTime);
            var (yaw, pitch) = AimMode == CameraAim.Target
                ? GetAngles(position, ToWorld(TargetX, TargetY, TargetZ, itemTime))
                : (Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime));

            return new CameraState(position, yaw, pitch, roll, fieldOfView);
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

        public SceneMarker GetMarker(in FrameContext itemTime)
            => SceneMarker.ForCamera(GetState(itemTime).Position);

        public void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope)
            => Move(CameraMove.Translate(shift), itemTime, scope);

        public void Move(in CameraMove move, in FrameContext itemTime, in EditScope scope)
        {
            scope.Nudge(Roll, move.Roll);

            if (AimMode == CameraAim.Orbit)
            {
                MoveOrbit(move, itemTime, scope);
                return;
            }

            scope.Nudge(X, WorldScale.ToPixels(move.Shift.X));
            scope.Nudge(Y, -WorldScale.ToPixels(move.Shift.Y));
            scope.Nudge(Z, WorldScale.ToPixels(move.Shift.Z));

            if (AimMode == CameraAim.Target)
                return;

            scope.Nudge(Yaw, move.Yaw);
            scope.Nudge(Pitch, move.Pitch);
        }

        private void MoveOrbit(in CameraMove move, in FrameContext itemTime, in EditScope scope)
        {
            // 回り込みでは、角度が決まればカメラの居場所も決まる。回した分に付いてくる
            // 平行移動を受け取ると、注目点まで一緒にずれてしまうので捨てる。
            if (move.Yaw != 0f || move.Pitch != 0f)
            {
                scope.Nudge(Yaw, move.Yaw);
                scope.Nudge(Pitch, move.Pitch);
                return;
            }

            if (move.Shift == Vector3.Zero)
                return;

            // 角度が動かないときだけ、視線に沿った分を距離に、残りを注目点に渡す。
            var forward = GetState(itemTime).Forward;
            var along = Vector3.Dot(move.Shift, forward);
            var across = move.Shift - forward * along;

            scope.Nudge(Distance, -WorldScale.ToPixels(along));
            scope.Nudge(TargetX, WorldScale.ToPixels(across.X));
            scope.Nudge(TargetY, -WorldScale.ToPixels(across.Y));
            scope.Nudge(TargetZ, WorldScale.ToPixels(across.Z));
        }

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [X, Y, Z, TargetX, TargetY, TargetZ, Distance, Yaw, Pitch, Roll, FieldOfView];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription)
            => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }

    public enum CameraAim
    {
        [Display(Name = "位置", Description = "置いた場所から、水平・垂直の回転角で向きを決めます")]
        Rotation,

        [Display(Name = "注目", Description = "置いた場所から、決めた点を向き続けます。被写体を追うのに向きます")]
        Target,

        [Display(Name = "回り込み",
            Description = "決めた点のまわりを、距離と回転角で回り込みます。回り込みカメラと同じ決め方です")]
        Orbit,
    }
}
