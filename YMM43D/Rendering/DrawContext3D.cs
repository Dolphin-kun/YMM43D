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

        /// <summary>
        /// 描画に使用するテクスチャ（元の2D画像）。エフェクト等で使用します。
        /// </summary>
        public ID3D11ShaderResourceView? Texture { get; set; }

        /// <summary>
        /// Texture をこの DrawContext3D 側で破棄してよい場合は true。
        /// </summary>
        public bool OwnsTexture { get; set; }
    }
}
