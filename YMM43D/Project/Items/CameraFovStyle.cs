using System.ComponentModel.DataAnnotations;

namespace YMM43D.Project.Items
{
    public enum CameraFovStyle
    {
        [Display(Name = "画角を変える", Description = "狭くすると望遠、広くすると広角になります")]
        Angle,

        [Display(Name = "写る大きさを保つ", Description = "画角に合わせてカメラを前後させ、見つめる点の写る大きさを保ちます")]
        Distance,
    }
}
