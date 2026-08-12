using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Shape3D
{
    internal sealed class Shape3DParameter : ShapeParameter3DBase
    {
        [Display(GroupName = "", Name = "サイズ")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation Size { get; } = new(100, 0, 100000);

        [Display(GroupName = "", Name = "投影方法")]
        [EnumComboBox]
        public ProjectionType Projection
        {
            get => projection;
            set => Set(ref projection, value);
        }
        private ProjectionType projection;

        [Display(GroupName = "3D回転", Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = "3D回転", Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = "3D回転", Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        public Shape3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
        }

        public Shape3DParameter() : this(null)
        {
        }

        protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices)
            => new Shape3DSource(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
            => [Size, RotationX, RotationY, RotationZ, CameraSyncAnimation];

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
            public Animation Size { get; } = new(100, 0, 100000);
            public Animation RotationX { get; } = new(0, -100000, 100000);
            public Animation RotationY { get; } = new(0, -100000, 100000);
            public Animation RotationZ { get; } = new(0, -100000, 100000);
            public ProjectionType Projection { get; set; }

            public SharedData(Shape3DParameter parameter)
            {
                Size.CopyFrom(parameter.Size);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                Projection = parameter.Projection;
            }

            public void CopyTo(Shape3DParameter parameter)
            {
                parameter.Size.CopyFrom(Size);
                parameter.RotationX.CopyFrom(RotationX);
                parameter.RotationY.CopyFrom(RotationY);
                parameter.RotationZ.CopyFrom(RotationZ);
                parameter.Projection = Projection;
            }
        }
    }
}
