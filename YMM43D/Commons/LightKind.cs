using System.ComponentModel.DataAnnotations;

namespace YMM43D.Commons
{
    public enum LightKind
    {
        [Display(Name = "平行光", Description = "太陽のように、どこでも同じ向きから当たります")]
        Directional,

        [Display(Name = "点光源", Description = "電球のように、置いた場所から周りへ広がります")]
        Point,

        [Display(Name = "スポットライト", Description = "舞台の照明のように、円錐の形に絞って照らします")]
        Spot,
    }
}
