namespace YMM43D.Graphics
{
    /// <summary>
    /// 面のカリング方法。
    /// </summary>
    /// <remarks>
    /// <c>Vortice.Direct3D11.CullMode</c> は既定値が 0（無効値）なので、
    /// 構造体の既定値がそのまま「カリングなし」になるよう独自に定義しています。
    /// </remarks>
    public enum FaceCulling
    {
        /// <summary>両面を描画します。</summary>
        None = 0,

        /// <summary>背面をカリングし、前面のみ描画します。</summary>
        Back,

        /// <summary>前面をカリングし、背面のみ描画します。</summary>
        Front,
    }
}
