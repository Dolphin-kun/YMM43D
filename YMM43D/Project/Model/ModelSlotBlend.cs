using System.ComponentModel.DataAnnotations;

namespace YMM43D.Project.Model
{
    public enum ModelSlotBlend
    {
        [Display(Name = "モデルのまま", Description = "モデルに書かれた透け方を使います")]
        Model,

        [Display(Name = "そのまま", Description = "透けさせずに塗ります")]
        Opaque,

        [Display(Name = "切り抜く", Description = "薄いところを切り抜きます")]
        Mask,

        [Display(Name = "透かす", Description = "薄いところを半透明で重ねます")]
        Blend,
    }
}
