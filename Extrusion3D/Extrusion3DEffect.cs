using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Extrusion3D
{
    [VideoEffect("立体化3D", ["3D"], [])]
    public class Extrusion3DEffect : VideoEffect3DBase
    {
        public override string Label => "立体化3D";

        [Display(GroupName = "立体化3D", Name = "厚み", Description = "3D立体化の厚みを設定します")]
        [AnimationSlider("F0", "", 0, 100)]
        public Animation Thickness { get; } = new Animation(10, 0, 1000);

        [Display(GroupName = "立体化3D", Name = "側面", Description = "側面の種類")]
        [EnumComboBox]
        public ExtrusionType ExtrusionType { get => extrusionType; set => Set(ref extrusionType, value); }
        private ExtrusionType extrusionType = ExtrusionType.Image;

        [Display(GroupName = "立体化3D", Name = "減衰", Description = "側面の減衰の強さを設定します")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(ExtrusionType), ExtrusionType.Image)]
        public Animation Attenuation { get; } = new Animation(0, 0, 100);

        [Display(GroupName = "立体化3D", Name = "色", Description = "側面の塗りつぶし色を設定します")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(ExtrusionType), ExtrusionType.Solid)]
        public Color SideColor { get => sideColor; set => Set(ref sideColor, value); }
        private Color sideColor = Colors.White;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Thickness, Attenuation, .. GetBaseAnimatables()];

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            var processor = new Extrusion3DProcessor(this, devices);
            LastProvider = processor;
            return processor;
        }

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription)
        {
            return [];
        }

        public override bool RequiresMappedTexture => false;
    }
}
