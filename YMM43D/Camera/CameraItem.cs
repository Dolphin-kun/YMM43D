using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
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
        private const string Angle = "向き";
        private const string Look = "注視点";

        [Display(GroupName = Angle, Name = "水平回転", Description = "注視点のまわりを横に回ります")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Yaw { get; } = new(0, -100000, 100000);

        [Display(GroupName = Angle, Name = "垂直回転", Description = "注視点のまわりを縦に回ります")]
        [AnimationSlider("F1", "°", -90, 90)]
        public Animation Pitch { get; } = new(0, -CameraMove.MaxPitch, CameraMove.MaxPitch);

        [Display(GroupName = Angle, Name = "傾き", Description = "視線を軸にして画面を回します")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Roll { get; } = new(0, -100000, 100000);

        [Display(GroupName = Angle, Name = "遠近の強さ",
            Description = "寄り引きではありません。注視点の面にあるものは大きさが変わらず、手前と奥の差だけが変わります")]
        [AnimationSlider("F1", "", 0.1, 100)]
        public Animation Distance { get; } = new(10, CameraMove.MinDistance, 1000);

        [Display(GroupName = Look, Name = "X", Description = "カメラが向く先。動かすと被写体を追えます")]
        [AnimationSlider("F0", "px", -1000, 1000)]
        public Animation TargetX { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Look, Name = "Y")]
        [AnimationSlider("F0", "px", -1000, 1000)]
        public Animation TargetY { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Look, Name = "Z")]
        [AnimationSlider("F0", "px", -1000, 1000)]
        public Animation TargetZ { get; } = new(0, -1000000, 1000000);

        public override string Label => "3Dカメラ";

        public override string Description => $"遠近 {Distance.GetFirstValue():F1}";

        public override Color ItemColor
        {
            get => itemColor;
            set => Set(ref itemColor, value);
        }
        private Color itemColor = Color.FromRgb(0x1E, 0x8A, 0x7A);

        // 中身を持たないアイテムなので、伸ばせる長さに上限は無い。
        // YMM4 の図形やテキストも同じく TimeSpan.Zero を返す。
        public override TimeSpan OriginalContentLength => TimeSpan.Zero;

        public override TimeSpan ContentLength => TimeSpan.Zero;

        public CameraState GetState(in FrameContext itemTime) => new(
            Yaw.GetFloat(itemTime),
            Pitch.GetFloat(itemTime),
            Roll.GetFloat(itemTime),
            Distance.GetFloat(itemTime),
            // 注視点はピクセルで指定させる。3D 空間の単位を意識せずに、他のアイテムの
            // 位置と同じ感覚で置けるようにするため。Y は画面と同じく下向きが正。
            new Vector3(
                WorldScale.ToWorld(TargetX.GetFloat(itemTime)),
                -WorldScale.ToWorld(TargetY.GetFloat(itemTime)),
                WorldScale.ToWorld(TargetZ.GetFloat(itemTime))));

        public void Move(in CameraMove move)
        {
            Nudge(Yaw, move.Yaw);
            Nudge(Pitch, move.Pitch);
            Nudge(Roll, move.Roll);
            Nudge(Distance, move.Distance);
            Nudge(TargetX, WorldScale.ToPixels(move.Target.X));
            Nudge(TargetY, -WorldScale.ToPixels(move.Target.Y));
            Nudge(TargetZ, WorldScale.ToPixels(move.Target.Z));
        }

        /// <summary>
        /// キーフレームを壊さずに、すべての値を同じだけ動かします。
        /// </summary>
        /// <remarks>
        /// <see cref="Animation.AddToEachValues"/> は上下限で丸めません。範囲の外へ
        /// 出た分がそのまま溜まるので、逆へドラッグしたときに同じ量だけ空回りします。
        /// 足せるぶんだけに削ってから渡します。
        /// </remarks>
        private static void Nudge(Animation animation, double delta)
        {
            if (delta == 0 || animation.Values is not { Count: > 0 } values)
                return;

            var room = Math.Min(animation.MaxValue - values.Max(v => v.Value), delta);
            room = Math.Max(animation.MinValue - values.Min(v => v.Value), room);

            // すでに範囲外にある場合、削った結果が向きごと反転することがある。
            if (room == 0 || Math.Sign(room) != Math.Sign(delta))
                return;

            animation.AddToEachValues(room);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Yaw, Pitch, Roll, Distance, TargetX, TargetY, TargetZ];

        public override IAsyncEnumerable<ExoItem> GetExoItemsAsync(ExoOutputDescription outputDescription)
            => AsyncEnumerable.Empty<ExoItem>();

        public override IEnumerable<string> GetFiles() => [];

        public override void ReplaceFile(string from, string to)
        {
        }
    }
}
