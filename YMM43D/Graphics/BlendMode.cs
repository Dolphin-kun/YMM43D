namespace YMM43D.Graphics
{
    public enum BlendMode
    {
        Normal,
        Add,
        Subtract,
        Multiply,
        Screen,

        // 乗算済みアルファのまま足し込む。重ねた枚数がそのまま濃さになる。
        Accumulate,
    }
}
