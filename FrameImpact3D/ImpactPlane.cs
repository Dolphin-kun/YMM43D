using System.ComponentModel.DataAnnotations;

namespace FrameImpact3D
{
    public enum ImpactPlane
    {
        [Display(Name = "画面に向ける", Description = "どこから見ても正面を向いた面")]
        Screen,

        [Display(Name = "床と平行", Description = "床に沿って横に広がる面")]
        Floor,

        [Display(Name = "アイテムの向き", Description = "アイテムの面に沿った面")]
        Item,
    }
}
