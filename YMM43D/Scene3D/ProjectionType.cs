using System.ComponentModel.DataAnnotations;

namespace YMM43D.Scene3D
{
    /// <summary>
    /// 3D空間を 2D に落とし込む方法。
    /// </summary>
    /// <remarks>
    /// 現時点では <see cref="SceneCamera.GetProjectionMatrix"/> が常に透視投影を返すため、
    /// この設定はまだ描画結果に反映されません。保存済みプロジェクトとの互換のために
    /// 定義だけ残しています。
    /// </remarks>
    public enum ProjectionType
    {
        [Display(Name = "透視投影")]
        Perspective,

        [Display(Name = "平行投影")]
        Orthographic,
    }
}
