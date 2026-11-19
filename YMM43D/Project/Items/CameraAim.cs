using System.ComponentModel.DataAnnotations;

namespace YMM43D.Project.Items
{
    public enum CameraAim
    {
        [Display(Name = "位置", Description = "置いた場所から、水平・垂直の回転角で向きを決めます")]
        Rotation,

        [Display(Name = "注目", Description = "置いた場所から決めた点を向き続けるので、被写体を追うのに向きます")]
        Target,

        [Display(Name = "回り込み", Description = "決めた点のまわりを、距離と回転角で回り込みます")]
        Orbit,
    }
}
