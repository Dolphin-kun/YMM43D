using System.ComponentModel.DataAnnotations;

namespace Extrusion3D
{
    public enum ExtrusionType
    {
        [Display(Name = "画像", Description = "")]
        Image = 1,
        [Display(Name = "単色", Description = "")]
        Solid = 2,
    }
}
