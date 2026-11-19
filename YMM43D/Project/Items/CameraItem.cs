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
        private const string Hand = "手ブレ";

        private const int AfterGeneral = 100;

        private const double DefaultZ = SceneProjection.DefaultFocalDistance * WorldScale.PixelsPerUnit;
        private const double DefaultFieldOfView = 57;
        private const float DriftIn = 0.35f;
        private const float RollShare = 0.6f;

        [Display(GroupName = Place, Name = "置き方", Description = "カメラの位置と向きの決め方", Order = AfterGeneral)]
        [EnumComboBox]
        public CameraAim AimMode { get; set => Set(ref field, value, nameof(AimMode), nameof(AimsByOrbit), nameof(HasPosition), nameof(HasTarget), nameof(HasAngles)); } = CameraAim.Target;

        public bool AimsByOrbit => AimMode == CameraAim.Orbit;

        public bool HasPosition => AimMode != CameraAim.Orbit;

        public bool HasTarget => AimMode != CameraAim.Rotation;

        public bool HasAngles => AimMode != CameraAim.Target;

        [Display(GroupName = Place, Name = "X", Description = "カメラを置く横の位置で、右が正です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(HasPosition), true)]
        public Animation X { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "Y", Description = "カメラを置く縦の位置で、下が正です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(HasPosition), true)]
        public Animation Y { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "Z", Description = "カメラを置く奥行きの位置で、手前が正、0 が YMM4 の 2D の面です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(HasPosition), true)]
        public Animation Z { get; } = new(DefaultZ, -1000000, 1000000);

        [Display(GroupName = Place, Name = "注目X", Description = "見つめる点の横の位置で、右が正です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(HasTarget), true)]
        public Animation TargetX { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "注目Y", Description = "見つめる点の縦の位置で、下が正です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -2000, 2000)]
        [ShowPropertyEditorWhen(nameof(HasTarget), true)]
        public Animation TargetY { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "注目Z", Description = "見つめる点の奥行きの位置で、手前が正、0 が YMM4 の 2D の面です", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", -5000, 5000)]
        [ShowPropertyEditorWhen(nameof(HasTarget), true)]
        public Animation TargetZ { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "距離", Description = "注目点からどれだけ離れて回り込むか", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", 100, 5000)]
        [ShowPropertyEditorWhen(nameof(AimsByOrbit), true)]
        public Animation Distance { get; } = new(DefaultZ, 1, 1000000);

        [Display(GroupName = Place, Name = "水平回転", Description = "カメラの左右の向き", Order = AfterGeneral)]
        [AnimationSlider("F1", "°", -180, 180)]
        [ShowPropertyEditorWhen(nameof(HasAngles), true)]
        public Animation Yaw { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "垂直回転", Description = "カメラの上下の向き", Order = AfterGeneral)]
        [AnimationSlider("F1", "°", -CameraState.MaxPitch, CameraState.MaxPitch)]
        [ShowPropertyEditorWhen(nameof(HasAngles), true)]
        public Animation Pitch { get; } = new(0, -CameraState.MaxPitch, CameraState.MaxPitch);

        [Display(GroupName = Place, Name = "傾き", Description = "視線を軸にして画面を回します", Order = AfterGeneral)]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Roll { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "視野角変更", Description = "入れると視野角を自分で決め、切ると Z=0 の面が YMM4 の画面と同じ大きさに写る画角になります", Order = AfterGeneral)]
        [ToggleSlider]
        public bool IsFieldOfViewEnabled { get; set => Set(ref field, value); }

        [Display(GroupName = Place, Name = "視野角", Description = "縦方向の画角", Order = AfterGeneral)]
        [AnimationSlider("F1", "°", 1, 179)]
        [ShowPropertyEditorWhen(nameof(IsFieldOfViewEnabled), true)]
        public Animation FieldOfView { get; } = new(DefaultFieldOfView, 1, 179);

        [Display(GroupName = Place, Name = "画角の変え方", Description = "画角を変えたときに、カメラを前後させるかどうか", Order = AfterGeneral)]
        [EnumComboBox]
        [ShowPropertyEditorWhen(nameof(IsFieldOfViewEnabled), true)]
        public CameraFovStyle FovStyle { get; set => Set(ref field, value); } = CameraFovStyle.Angle;

        [Display(GroupName = Hand, Name = "手ブレ", Description = "カメラを手で持ったように細かく揺らします", Order = AfterGeneral)]
        [ToggleSlider]
        public bool IsShakeEnabled { get; set => Set(ref field, value); }

        [Display(GroupName = Hand, Name = "位置のゆれ", Description = "カメラがどれだけ動くか", Order = AfterGeneral)]
        [AnimationSlider("F1", "px", 0, 50)]
        [ShowPropertyEditorWhen(nameof(IsShakeEnabled), true)]
        public Animation ShakeShift { get; } = new(6, 0, 1000000);

        [Display(GroupName = Hand, Name = "向きのゆれ", Description = "カメラがどれだけ振れるか", Order = AfterGeneral)]
        [AnimationSlider("F2", "°", 0, 5)]
        [ShowPropertyEditorWhen(nameof(IsShakeEnabled), true)]
        public Animation ShakeTurn { get; } = new(0.6, 0, 100000);

        [Display(GroupName = Hand, Name = "速さ", Description = "1 秒に何回ゆれるか", Order = AfterGeneral)]
        [AnimationSlider("F2", "回/秒", 0.1, 10)]
        [ShowPropertyEditorWhen(nameof(IsShakeEnabled), true)]
        public Animation ShakeSpeed { get; } = new(2.5, 0.01, 1000);

        [Display(GroupName = Hand, Name = "細かさ", Description = "細かいゆれをどれだけ混ぜるか", Order = AfterGeneral)]
        [TextBoxSlider("F0", "", CameraShake.LeastDetail, CameraShake.MostDetail)]
        [Range(CameraShake.LeastDetail, CameraShake.MostDetail)]
        [ShowPropertyEditorWhen(nameof(IsShakeEnabled), true)]
        public int ShakeDetail { get; set => Set(ref field, Math.Clamp(value, CameraShake.LeastDetail, CameraShake.MostDetail)); } = 2;

        [Display(GroupName = Hand, Name = "種", Description = "ゆれ方を決める乱数の種", Order = AfterGeneral)]
        [TextBoxSlider("F0", "", 0, 99)]
        [Range(0, 9999)]
        [ShowPropertyEditorWhen(nameof(IsShakeEnabled), true)]
        public int ShakeSeed { get; set => Set(ref field, Math.Clamp(value, 0, 9999)); }

        public override string Label => "3Dカメラ";

        public override Color ItemColor { get => itemColor; set => Set(ref itemColor, value); }
        private Color itemColor = Color.FromRgb(0xFF, 0x99, 0x00);

        public override TimeSpan OriginalContentLength => TimeSpan.Zero;

        public override TimeSpan ContentLength => TimeSpan.Zero;

        public CameraState GetState(in FrameContext itemTime) => Shaken(GetAim(itemTime), itemTime);

        public CameraState GetAim(in FrameContext itemTime)
        {
            var roll = Roll.GetFloat(itemTime);
            var fieldOfView = IsFieldOfViewEnabled ? FieldOfView.GetFloat(itemTime) : 0f;
            var pulled = Pulled(fieldOfView);

            if (AimMode == CameraAim.Orbit)
            {
                var pitch = Math.Clamp(Pitch.GetFloat(itemTime), -CameraState.MaxPitch, CameraState.MaxPitch);
                var aimed = new CameraState(Vector3.Zero, Yaw.GetFloat(itemTime), pitch, roll, fieldOfView);
                var back = aimed.Forward * WorldScale.ToWorld(Distance.GetFloat(itemTime)) * pulled;

                return aimed with { Position = ToWorld(TargetX, TargetY, TargetZ, itemTime) - back };
            }

            var position = ToWorld(X, Y, Z, itemTime);

            if (AimMode == CameraAim.Target)
            {
                var target = ToWorld(TargetX, TargetY, TargetZ, itemTime);
                var (turned, raised) = GetAngles(position, target);
                var apart = Vector3.Distance(position, target);
                var looked = new CameraState(position, turned, raised, roll, fieldOfView);

                return apart > 1e-6f ? looked with { Position = target - looked.Forward * apart * pulled } : looked;
            }

            var free = new CameraState(position, Yaw.GetFloat(itemTime), Pitch.GetFloat(itemTime), roll, fieldOfView);

            return pulled == 1f ? free : free with { Position = position + free.Forward * SceneProjection.DefaultFocalDistance * (1f - pulled) };
        }

        private float Pulled(float fieldOfView)
        {
            if (!IsFieldOfViewEnabled || FovStyle != CameraFovStyle.Distance || fieldOfView <= 0f)
                return 1f;

            var wide = MathF.Tan(float.DegreesToRadians((float)DefaultFieldOfView / 2f));
            var now = MathF.Tan(float.DegreesToRadians(Math.Clamp(fieldOfView, 1f, 179f) / 2f));

            return now > 1e-6f ? wide / now : 1f;
        }

        private static Vector3 ToWorld(Animation x, Animation y, Animation z, in FrameContext itemTime) => WorldScale.ToWorldPosition(x.GetFloat(itemTime), y.GetFloat(itemTime), z.GetFloat(itemTime));

        private static (float Yaw, float Pitch) GetAngles(in Vector3 from, in Vector3 to)
        {
            var direction = to - from;

            if (direction.LengthSquared() < 1e-8f)
                return (0f, 0f);

            direction = Vector3.Normalize(direction);

            var pitch = float.RadiansToDegrees(MathF.Asin(Math.Clamp(direction.Y, -1f, 1f)));
            var yaw = float.RadiansToDegrees(MathF.Atan2(-direction.X, -direction.Z));

            return (yaw, Math.Clamp(pitch, -CameraState.MaxPitch, CameraState.MaxPitch));
        }

        public Vector3? GetPivot(in FrameContext itemTime) => AimMode is CameraAim.Target or CameraAim.Orbit ? ToWorld(TargetX, TargetY, TargetZ, itemTime) : null;

        public SceneMarker GetMarker(in FrameContext itemTime) => SceneMarker.ForCamera(GetAim(itemTime).Position);

        public void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope) => Move(CameraMove.Translate(shift), itemTime, scope);

        public void Move(in CameraMove move, in FrameContext itemTime, in EditScope scope)
        {
            scope.Nudge(Roll, move.Roll);

            if (AimMode == CameraAim.Orbit)
            {
                MoveOrbit(move, itemTime, scope);
                return;
            }

            scope.NudgePosition(X, Y, Z, move.Shift);

            if (AimMode == CameraAim.Target)
                return;

            scope.Nudge(Yaw, move.Yaw);
            scope.Nudge(Pitch, move.Pitch);
        }

        private void MoveOrbit(in CameraMove move, in FrameContext itemTime, in EditScope scope)
        {
            if (move.Yaw != 0f || move.Pitch != 0f)
            {
                scope.Nudge(Yaw, move.Yaw);
                scope.Nudge(Pitch, move.Pitch);
                return;
            }

            if (move.Shift == Vector3.Zero)
                return;

            var forward = GetAim(itemTime).Forward;
            var along = Vector3.Dot(move.Shift, forward);
            var across = move.Shift - forward * along;

            scope.Nudge(Distance, -WorldScale.ToPixels(along));
            scope.NudgePosition(TargetX, TargetY, TargetZ, across);
        }

        public CameraState Shaken(in CameraState state, in FrameContext itemTime)
        {
            if (!IsShakeEnabled || itemTime.Fps <= 0)
                return state;

            var shift = WorldScale.ToWorld(ShakeShift.GetFloat(itemTime));
            var turn = ShakeTurn.GetFloat(itemTime);

            if (shift <= 0f && turn <= 0f)
                return state;

            var at = itemTime.Frame / (float)itemTime.Fps * ShakeSpeed.GetFloat(itemTime);
            var wobble = CameraShake.Wobble(at, ShakeDetail, ShakeSeed);
            var tilt = CameraShake.Wobble(at, ShakeDetail, ShakeSeed + 29);

            var rotation = state.Rotation;
            var right = Vector3.Transform(Vector3.UnitX, rotation);
            var up = Vector3.Transform(Vector3.UnitY, rotation);
            var forward = Vector3.Transform(-Vector3.UnitZ, rotation);
            var drift = (right * wobble.X + up * wobble.Y + forward * wobble.Z * DriftIn) * shift;

            return state with
            {
                Position = state.Position + drift,
                Yaw = state.Yaw + tilt.X * turn,
                Pitch = Math.Clamp(state.Pitch + tilt.Y * turn, -CameraState.MaxPitch, CameraState.MaxPitch),
                Roll = state.Roll + tilt.Z * turn * RollShare,
            };
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [X, Y, Z, TargetX, TargetY, TargetZ, Distance, Yaw, Pitch, Roll, FieldOfView, ShakeShift, ShakeTurn, ShakeSpeed];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription) => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }
}
