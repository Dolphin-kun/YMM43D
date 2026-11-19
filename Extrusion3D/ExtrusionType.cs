using System.ComponentModel.DataAnnotations;

namespace Extrusion3D
{
    public enum ExtrusionType
    {
        [Display(Name = "画像", Description = "画像のふちの色を奥へ伸ばして塗ります")]
        Image,

        [Display(Name = "単色", Description = "指定した色で塗ります")]
        Solid,
    }
}
