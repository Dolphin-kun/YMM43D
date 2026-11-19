using System.ComponentModel.DataAnnotations;

namespace FrameImpact3D
{
    public enum ImpactEmission
    {
        [Display(Name = "全方向", Description = "中心からまわり全体へ広がる")]
        All,

        [Display(Name = "向きを決める", Description = "決めた角度の向きへ、広がりの幅で飛ぶ")]
        Aimed,
    }
}
