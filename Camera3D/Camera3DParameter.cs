using System.ComponentModel.DataAnnotations;
using System.Numerics;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Camera3D
{
    internal sealed class Camera3DParameter : ShapeParameterBase, ISceneCameraSource
    {
        private const string Angle = "向き";
        private const string Look = "注視点";

        [Display(GroupName = Angle, Name = "水平回転", Description = "注視点のまわりを横に回ります")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Yaw { get; } = new(0, -100000, 100000);

        [Display(GroupName = Angle, Name = "垂直回転", Description = "注視点のまわりを縦に回ります")]
        [AnimationSlider("F1", "°", -90, 90)]
        public Animation Pitch { get; } = new(0, -90, 90);

        [Display(GroupName = Angle, Name = "傾き", Description = "視線を軸にして画面を回します")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Roll { get; } = new(0, -100000, 100000);

        [Display(GroupName = Angle, Name = "遠近の強さ",
            Description = "寄り引きではありません。注視点の面にあるものは大きさが変わらず、手前と奥の差だけが変わります")]
        [AnimationSlider("F1", "", 0.1, 100)]
        public Animation Distance { get; } = new(10, 0.1, 1000);

        [Display(GroupName = Look, Name = "X", Description = "カメラが向く先。動かすと被写体を追えます")]
        [AnimationSlider("F0", "px", -1000, 1000)]
        public Animation TargetX { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Look, Name = "Y")]
        [AnimationSlider("F0", "px", -1000, 1000)]
        public Animation TargetY { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Look, Name = "Z")]
        [AnimationSlider("F0", "px", -1000, 1000)]
        public Animation TargetZ { get; } = new(0, -1000000, 1000000);

        public Camera3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
        }

        public Camera3DParameter() : this(null)
        {
        }

        public CameraState GetCameraState(in FrameContext itemTime) => new(
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

        public override IShapeSource CreateShapeSource(IGraphicsDevicesAndContext devices)
            => new Camera3DSource(devices);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Yaw, Pitch, Roll, Distance, TargetX, TargetY, TargetZ];

        public override IEnumerable<string> CreateMaskExoFilter(
            int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(
            int keyFrameIndex, ExoOutputDescription desc) => [];

        protected override void LoadSharedData(SharedDataStore store)
            => store.Load<SharedData>()?.CopyTo(this);

        protected override void SaveSharedData(SharedDataStore store)
            => store.Save(new SharedData(this));

        private sealed class SharedData
        {
            // 範囲は本体のプロパティと必ず同じにする。狭いと、引き継ぐときに
            // 値が範囲の端まで丸められて戻ってくる。
            public Animation Yaw { get; } = new(0, -100000, 100000);
            public Animation Pitch { get; } = new(0, -90, 90);
            public Animation Roll { get; } = new(0, -100000, 100000);
            public Animation Distance { get; } = new(10, 0.1, 1000);
            public Animation TargetX { get; } = new(0, -1000000, 1000000);
            public Animation TargetY { get; } = new(0, -1000000, 1000000);
            public Animation TargetZ { get; } = new(0, -1000000, 1000000);

            public SharedData(Camera3DParameter parameter)
            {
                Yaw.CopyFrom(parameter.Yaw);
                Pitch.CopyFrom(parameter.Pitch);
                Roll.CopyFrom(parameter.Roll);
                Distance.CopyFrom(parameter.Distance);
                TargetX.CopyFrom(parameter.TargetX);
                TargetY.CopyFrom(parameter.TargetY);
                TargetZ.CopyFrom(parameter.TargetZ);
            }

            public void CopyTo(Camera3DParameter parameter)
            {
                parameter.Yaw.CopyFrom(Yaw);
                parameter.Pitch.CopyFrom(Pitch);
                parameter.Roll.CopyFrom(Roll);
                parameter.Distance.CopyFrom(Distance);
                parameter.TargetX.CopyFrom(TargetX);
                parameter.TargetY.CopyFrom(TargetY);
                parameter.TargetZ.CopyFrom(TargetZ);
            }
        }
    }
}
