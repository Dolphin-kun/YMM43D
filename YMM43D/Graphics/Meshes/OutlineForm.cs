using System.ComponentModel.DataAnnotations;

namespace YMM43D.Graphics.Meshes
{
    public enum OutlineForm
    {
        [Display(Name = "押し出す", Description = "下書きの形を奥行きへ押し出します")]
        Extrude,

        [Display(Name = "回す", Description = "下書きの形を縦の軸のまわりに回します")]
        Lathe,
    }
}
