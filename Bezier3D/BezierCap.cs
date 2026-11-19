using System.ComponentModel.DataAnnotations;

namespace Bezier3D
{
    public enum BezierCap
    {
        [Display(Name = "平ら", Description = "端でまっすぐ切ります")]
        Flat,

        [Display(Name = "四角", Description = "太さの半分だけ伸ばして切ります")]
        Square,

        [Display(Name = "丸", Description = "半球で閉じます")]
        Round,

        [Display(Name = "とがり", Description = "円錐で閉じます")]
        Point,
    }
}
