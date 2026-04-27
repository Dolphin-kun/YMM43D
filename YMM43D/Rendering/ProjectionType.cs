using System.ComponentModel.DataAnnotations;

namespace YMM43D.Rendering
{
    public enum ProjectionType
    {
        [Display(Name = "透視投影")]
        Perspective,
        [Display(Name = "平行投影")]
        Orthographic,
    }
}
