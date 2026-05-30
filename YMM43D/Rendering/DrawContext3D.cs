using System.Numerics;
using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public class DrawContext3D
    {
        public Matrix4x4 World { get; set; }
        public float Opacity { get; set; }
        
        public YukkuriMovieMaker.Project.Blend Blend { get; set; } = YukkuriMovieMaker.Project.Blend.Normal;
        
        public bool IsInverted { get; set; }
        public bool IsAlwaysOnTop { get; set; }
        public bool IsZOrderEnabled { get; set; }
        public bool IsClippingWithObjectAbove { get; set; }
        
        public int Frame { get; set; }
        public int Length { get; set; }
        public int FPS { get; set; }

        public ID3D11ShaderResourceView? Texture { get; set; }

        public bool OwnsTexture { get; set; }
    }
}
