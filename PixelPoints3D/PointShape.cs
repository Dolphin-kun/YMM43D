using System.ComponentModel.DataAnnotations;

namespace PixelPoints3D
{
    public enum PointShape
    {
        [Display(Name = "四角形", Description = "四角い粒を描きます")]
        Square,

        [Display(Name = "円", Description = "丸い粒を描きます")]
        Circle,
    }
}
