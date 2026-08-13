using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Camera
{
    /// <summary>
    /// シーン全体を撮る 3D カメラ。タイムラインに置いて使います。
    /// </summary>
    /// <remarks>
    /// 映像には何も描きません。置いてある区間だけ、3D の描画がこのカメラの視点に
    /// 切り替わります。図形アイテムではなく独立したアイテムなのは、位置・拡大率・
    /// 不透明度といった図形の設定がカメラには意味を持たないためです。
    /// <para>
    /// 同じ時刻に複数置けます。レイヤー番号がいちばん大きいものが使われるので、
    /// 時間で並べればカット割りになります。
    /// </para>
    /// </remarks>
    public sealed class CameraItem : BaseItem, ISceneCamera
    {
        private const string Place = "カメラ位置";

        // BaseItem の「全般」は 0〜5 を使う。項目の並びはこの値で決まるので、
        // それより後ろの番号にしないとカメラの設定が上に来てしまう。
        private const int FirstOrder = 100;

        [Display(GroupName = Place, Name = "X", Description = "カメラを置く位置。右が正", Order = FirstOrder)]
        [AnimationSlider("F0", "px", -2000, 2000)]
        public Animation X { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "Y", Description = "下が正。画面の座標と同じ向き", Order = FirstOrder + 1)]
        [AnimationSlider("F0", "px", -2000, 2000)]
        public Animation Y { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Place, Name = "Z", Description = "手前が正。0 の面が、YMM4 が 2D で描く面", Order = FirstOrder + 2)]
        [AnimationSlider("F0", "px", -5000, 5000)]
        public Animation Z { get; } = new(DefaultZ, -1000000, 1000000);

        [Display(GroupName = Place, Name = "水平回転", Description = "横を向きます", Order = FirstOrder + 3)]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Yaw { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "垂直回転", Description = "上下を向きます", Order = FirstOrder + 4)]
        [AnimationSlider("F1", "°", -CameraState.MaxPitch, CameraState.MaxPitch)]
        public Animation Pitch { get; } = new(0, -CameraState.MaxPitch, CameraState.MaxPitch);

        [Display(GroupName = Place, Name = "傾き", Description = "視線を軸にして画面を回します", Order = FirstOrder + 5)]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Roll { get; } = new(0, -100000, 100000);

        [Display(GroupName = Place, Name = "視野角変更",
            Description = "切ると、Z=0 の面が YMM4 の画面とちょうど同じ大きさに写る画角になります",
            Order = FirstOrder + 6)]
        [ToggleSlider]
        public bool IsFieldOfViewEnabled
        {
            get => isFieldOfViewEnabled;
            set => Set(ref isFieldOfViewEnabled, value);
        }
        private bool isFieldOfViewEnabled;

        [Display(GroupName = Place, Name = "視野角", Description = "縦方向の画角。狭いほど望遠になります",
            Order = FirstOrder + 7)]
        [AnimationSlider("F1", "°", 1, 179)]
        [ShowPropertyEditorWhen(nameof(IsFieldOfViewEnabled), true)]
        public Animation FieldOfView { get; } = new(DefaultFieldOfView, 1, 179);

        public override string Label => "3Dカメラ";

        public override Color ItemColor
        {
            get => itemColor;
            set => Set(ref itemColor, value);
        }
        // 3Dプレビューに出るカメラの枠と同じ色にする。どのアイテムがどの枠か分かる。
        private Color itemColor = Color.FromRgb(0xFF, 0x99, 0x00);

        // 中身を持たないアイテムなので、伸ばせる長さに上限は無い。
        // YMM4 の図形やテキストも同じく TimeSpan.Zero を返す。
        public override TimeSpan OriginalContentLength => TimeSpan.Zero;

        public override TimeSpan ContentLength => TimeSpan.Zero;

        /// <summary>Z の初期値。原点を正面から見る位置に置く。</summary>
        private const double DefaultZ = SceneProjection.DefaultFocalDistance * WorldScale.PixelsPerUnit;

        /// <summary>画角を使い始めたときに、見え方があまり変わらない値。</summary>
        private const double DefaultFieldOfView = 57;

        public CameraState GetState(in FrameContext itemTime) => new(
            // 位置はピクセルで指定させる。3D 空間の単位を意識せずに、他のアイテムの
            // 位置と同じ感覚で置けるようにするため。Y は画面と同じく下向きが正。
            new Vector3(
                WorldScale.ToWorld(X.GetFloat(itemTime)),
                -WorldScale.ToWorld(Y.GetFloat(itemTime)),
                WorldScale.ToWorld(Z.GetFloat(itemTime))),
            Yaw.GetFloat(itemTime),
            Pitch.GetFloat(itemTime),
            Roll.GetFloat(itemTime),
            IsFieldOfViewEnabled ? FieldOfView.GetFloat(itemTime) : 0f);

        public void Move(in CameraMove move)
        {
            Yaw.Nudge(move.Yaw);
            Pitch.Nudge(move.Pitch);
            Roll.Nudge(move.Roll);
            X.Nudge(WorldScale.ToPixels(move.Shift.X));
            Y.Nudge(-WorldScale.ToPixels(move.Shift.Y));
            Z.Nudge(WorldScale.ToPixels(move.Shift.Z));
        }

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [X, Y, Z, Yaw, Pitch, Roll, FieldOfView];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription)
            => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }
}
