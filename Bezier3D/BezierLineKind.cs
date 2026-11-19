using System.ComponentModel.DataAnnotations;

namespace Bezier3D
{
    public enum BezierLineKind
    {
        [Display(Name = "直線", Description = "点をまっすぐ結びます")]
        Straight,

        [Display(Name = "2次ベジェ", Description = "点を「通る点・ひっぱる点」の順に使います")]
        Quadratic,

        [Display(Name = "3次ベジェ", Description = "点を「通る点・ひっぱる点2つ」の順に使います")]
        Cubic,
    }
}
