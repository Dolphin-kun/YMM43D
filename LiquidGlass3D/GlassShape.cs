using System.ComponentModel.DataAnnotations;

namespace LiquidGlass3D
{
    public enum GlassShape
    {
        [Display(Name = "角丸の箱", Description = "角を丸めた直方体")]
        RoundedBox,

        [Display(Name = "球", Description = "球")]
        Sphere,

        [Display(Name = "円柱", Description = "縦に立てた円柱")]
        Cylinder,

        [Display(Name = "ドーナツ", Description = "真ん中に穴の空いた輪")]
        Torus,

        [Display(Name = "正八面体", Description = "8 枚の正三角形でできた形")]
        Octahedron,
    }
}
