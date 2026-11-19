using System.ComponentModel.DataAnnotations;

namespace FrameImpact3D
{
    public enum ImpactArea
    {
        [Display(Name = "画面全体", Description = "アイテム全体を塗り替える")]
        Whole,

        [Display(Name = "中心のまわり", Description = "中心から半径の内側だけを塗り替える")]
        AroundCenter,
    }
}
