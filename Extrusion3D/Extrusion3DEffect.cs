using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    public class Extrusion3DEffect : VideoEffectBase, I3DProvider, I3DTextureProvider
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

        public Extrusion3DProcessor? LastProcessor { get; private set; }
        private Extrusion3DSource? sharedSource;

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription)
        {
            return [];
        }

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            LastProcessor = new Extrusion3DProcessor(this, devices);
            if (sharedSource == null || sharedSource.processor != LastProcessor)
            {
                sharedSource = new Extrusion3DSource(this, LastProcessor);
            }
            return LastProcessor;
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [ Thickness, Attenuation ];

        public void Draw(ID3D11Device device,ID3D11DeviceContext d3dDc, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection, DrawContext3D drawContext)
        {
            sharedSource?.Draw(device, d3dDc, view, projection, drawContext);
        }

        // I3DTextureProvider: PropertyMapper が item.VideoEffects から呼ぶ
        public ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            return LastProcessor?.GetTexture(device);
        }
    }
}
