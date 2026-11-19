using System.ComponentModel.DataAnnotations;

namespace PixelPoints3D
{
    public enum DeformKind
    {
        [Display(Name = "なし", Description = "歪ませません")]
        None,

        [Display(Name = "波", Description = "選んだ軸に沿って進む波で、奥行き方向に押し引きします")]
        Wave,

        [Display(Name = "ねじれ", Description = "選んだ軸のまわりに、進むほど強くひねります")]
        Twist,

        [Display(Name = "膨らみ", Description = "軸の中心ほど大きく持ち上げて、丸く盛り上げます")]
        Bulge,

        [Display(Name = "球に巻く", Description = "平らな並びを、選んだ軸を極とする球の面に貼り付けます")]
        Sphere,
    }
}
