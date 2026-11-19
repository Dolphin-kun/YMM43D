using System.ComponentModel.DataAnnotations;

namespace PixelPoints3D
{
    public enum PointColorSource
    {
        [Display(Name = "単色", Description = "指定した色で塗ります")]
        Solid,

        [Display(Name = "画像", Description = "その位置の画像の色を使います")]
        Image,
    }
}
