using System.ComponentModel.DataAnnotations;

namespace Bezier3D
{
    public enum BezierDashStyle
    {
        [Display(Name = "実線", Description = "途切れのない線")]
        Solid,

        [Display(Name = "破線", Description = "同じ長さの線と空白をくり返します")]
        Dash,

        [Display(Name = "点線", Description = "点と空白をくり返します")]
        Dot,

        [Display(Name = "一点鎖線", Description = "線・点の順にくり返します")]
        DashDot,

        [Display(Name = "二点鎖線", Description = "線・点・点の順にくり返します")]
        DashDotDot,

        [Display(Name = "カスタム", Description = "破線のパターンで並びを決めます")]
        Custom,
    }
}
