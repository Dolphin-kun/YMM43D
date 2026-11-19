using System.ComponentModel.DataAnnotations;

namespace PixelPoints3D
{
    public enum DeformAxis
    {
        [Display(Name = "X軸", Description = "横")]
        X,

        [Display(Name = "Y軸", Description = "縦")]
        Y,

        [Display(Name = "Z軸", Description = "奥行き")]
        Z,
    }
}
