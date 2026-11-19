namespace YMM43D.PreviewTool
{
    [Flags]
    internal enum SnapTargets
    {
        None = 0,

        Increment = 1,

        Vertex = 2,

        Edge = 4,

        Face = 8,
    }
}
