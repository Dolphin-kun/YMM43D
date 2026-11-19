using System.ComponentModel.DataAnnotations;

namespace Bezier3D
{
    public enum BezierSection
    {
        [Display(Name = "丸", Description = "管になります")]
        Round,

        [Display(Name = "平たい", Description = "帯になります")]
        Flat,
    }
}
