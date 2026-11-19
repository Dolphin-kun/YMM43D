using System.ComponentModel.DataAnnotations;

namespace Noise3D
{
    public enum Noise3DFractalMode
    {
        [Display(Name = "通常", Description = "各段のノイズをそのまま重ねます")]
        Normal,

        [Display(Name = "乱流", Description = "各段のノイズの絶対値を重ねて、もくもくした模様にします")]
        Turbulence,

        [Display(Name = "リッジ", Description = "各段のノイズを反転して重ねて、尾根のような鋭い筋を作ります")]
        Ridged,
    }
}
