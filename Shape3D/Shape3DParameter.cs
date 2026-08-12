using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Shape3D
{
    internal sealed class Shape3DParameter : ShapeParameter3DBase
    {
        private const string Body = "";
        private const string Rotation = "3D回転";
        private const string Paint = "色";

        [Display(GroupName = Body, Name = "形", Description = "何面体を描くか")]
        [EnumComboBox]
        public SolidKind Solid
        {
            get => solid;
            set
            {
                Set(ref solid, value);
                OnPropertyChanged(nameof(FaceCount));
            }
        }
        private SolidKind solid = SolidKind.Cube;

        [Display(GroupName = Body, Name = "サイズ", Description = "いちばん長い差し渡し。六面体では一辺の長さ")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation Size { get; } = new(100, 0, 100000);

        [Display(GroupName = Rotation, Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationX { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationY { get; } = new(0, -100000, 100000);

        [Display(GroupName = Rotation, Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RotationZ { get; } = new(0, -100000, 100000);

        [Display(GroupName = Paint, Name = "塗り", Description = "全体を1色で塗るか、面ごとに色を変えるか")]
        [EnumComboBox]
        public CubeFill Fill { get => fill; set => Set(ref fill, value); }
        private CubeFill fill = CubeFill.Solid;

        [Display(GroupName = Paint, Name = "色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Fill), CubeFill.Solid)]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        [Display(AutoGenerateField = true)]
        public ImmutableList<FaceColor> FaceColors { get => faceColors; set => Set(ref faceColors, value); }
        private ImmutableList<FaceColor> faceColors = [];

        /// <summary>
        /// 面ごとの色の控え。編集画面には出しません。
        /// </summary>
        /// <remarks>
        /// 単色のあいだは <see cref="FaceColors"/> を空にして一覧ごと隠すため、
        /// 選んだ色をここに残しておきます。面ごとに戻したときに元の色が復活します。
        /// </remarks>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public ImmutableList<Color> StoredFaceColors { get => storedFaceColors; set => Set(ref storedFaceColors, value); }
        private ImmutableList<Color> storedFaceColors = [];

        /// <summary>いま選んでいる多面体の面の数。</summary>
        internal int FaceCount => Polyhedron.FaceCountOf(Solid);

        /// <summary>面の並び順に並べた、実際に塗る色。</summary>
        internal IReadOnlyList<Color> ResolvedColors => Fill == CubeFill.Solid
            ? [Color]
            : FaceColors.Count > 0 ? [.. FaceColors.Select(f => f.Color)] : StoredFaceColors;

        public Shape3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
            SubscribeChildUndoRedoable(FaceColors);
        }

        public Shape3DParameter() : this(null)
        {
        }

        public override async ValueTask EndEditAsync()
        {
            await base.EndEditAsync();

            SyncFaceColors();
        }

        /// <summary>
        /// 面ごとの色の一覧を、いまの塗り方と面の数に合わせます。
        /// </summary>
        private void SyncFaceColors()
        {
            // 単色のあいだは一覧を空にする。項目が無ければ編集画面にも出ない。
            if (Fill == CubeFill.Solid)
            {
                if (FaceColors.Count == 0)
                    return;

                StoredFaceColors = [.. FaceColors.Select(f => f.Color)];
                FaceColors = [];
                return;
            }

            var current = FaceColors.Count > 0
                ? FaceColors.Select(f => f.Color).ToImmutableList()
                : StoredFaceColors;
            var wanted = Resize(FaceCount, current);

            if (!wanted.SequenceEqual(FaceColors.Select(f => f.Color)))
                FaceColors = [.. wanted.Select(c => new FaceColor { Color = c })];

            if (!wanted.SequenceEqual(StoredFaceColors))
                StoredFaceColors = wanted;
        }

        /// <summary>
        /// 面の数に合わせて、色の数を増減します。
        /// </summary>
        /// <remarks>
        /// 減らすときは後ろを落とすだけなので、面の多い形へ戻せば前の色が残ります。
        /// 増やす分は、向きが読めるよう明るさを散らした無彩色にします。
        /// </remarks>
        private static ImmutableList<Color> Resize(int count, ImmutableList<Color> current)
        {
            if (current.Count == count)
                return current;

            if (current.Count > count)
                return current.GetRange(0, count);

            var grown = current.ToBuilder();
            for (var i = current.Count; i < count; i++)
                grown.Add(DefaultColorAt(i, count));

            return grown.ToImmutable();
        }

        private static Color DefaultColorAt(int face, int count)
        {
            // 面の数が変わっても端が白と暗灰色になるよう、割合で決める。
            var level = (byte)(0x60 + 0x9F * face / Math.Max(1, count - 1));

            return Color.FromRgb(level, level, level);
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
            public SolidKind Solid { get; set; }
            public CubeFill Fill { get; set; }
            public Color Color { get; set; }
            public ImmutableList<Color> FaceColors { get; set; } = [];
            public ImmutableList<Color> StoredFaceColors { get; set; } = [];

            public SharedData(Shape3DParameter parameter)
            {
                Size.CopyFrom(parameter.Size);
                RotationX.CopyFrom(parameter.RotationX);
                RotationY.CopyFrom(parameter.RotationY);
                RotationZ.CopyFrom(parameter.RotationZ);
                Solid = parameter.Solid;
                Fill = parameter.Fill;
                Color = parameter.Color;
                FaceColors = [.. parameter.FaceColors.Select(f => f.Color)];
                StoredFaceColors = parameter.StoredFaceColors;
            }

            public void CopyTo(Shape3DParameter parameter)
            {
                parameter.Size.CopyFrom(Size);
                parameter.RotationX.CopyFrom(RotationX);
                parameter.RotationY.CopyFrom(RotationY);
                parameter.RotationZ.CopyFrom(RotationZ);
                parameter.Solid = Solid;
                parameter.Fill = Fill;
                parameter.Color = Color;
                parameter.FaceColors = [.. FaceColors.Select(c => new FaceColor { Color = c })];
                parameter.StoredFaceColors = StoredFaceColors;
            }
        }
    }

    public class FaceColor : Animatable
    {
        [Display(GroupName = "色", Name = "面", Description = "上から順に、面の並びと同じ順です")]
        [ColorPicker]
        public Color Color { get => color; set => Set(ref color, value); }
        private Color color = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }

    public enum CubeFill
    {
        [Display(Name = "単色", Description = "すべての面を同じ色で塗ります")]
        Solid,

        [Display(Name = "面ごと", Description = "面それぞれに色を指定します")]
        PerFace,
    }
}
