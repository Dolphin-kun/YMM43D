using System.ComponentModel.DataAnnotations;

namespace YMM43D.Project.Shape
{
    public enum CubeFill
    {
        [Display(Name = "単色", Description = "すべての面を同じ色で塗ります")]
        Solid,

        [Display(Name = "面ごと", Description = "面それぞれに色を指定します")]
        PerFace,

        [Display(Name = "材質", Description = "面ごとに材質（画像・つや・金属っぽさ・光る色）を割り当てて塗ります")]
        Material,
    }
}
