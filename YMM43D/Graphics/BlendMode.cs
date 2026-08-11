namespace YMM43D.Graphics
{
    /// <summary>
    /// 3D描画時の合成方法。
    /// YMM4 の <c>YukkuriMovieMaker.Project.Blend</c> と同じ内容ですが、
    /// Graphics 層を YMM4 のプロジェクトモデルから独立させるため別に定義しています。
    /// 変換は上位層（Scene / Plugin）で行います。
    /// </summary>
    public enum BlendMode
    {
        Normal,
        Add,
        Subtract,
        Multiply,
        Screen,
    }
}
