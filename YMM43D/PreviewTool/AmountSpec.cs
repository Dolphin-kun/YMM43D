using System.Numerics;

namespace YMM43D.PreviewTool
{
    internal readonly record struct AmountSpec(string Label, AmountUnit Unit, float Start, bool Signed = false, string CountLabel = "", int Count = 0, int LeastCount = 0, string ShiftLabel = "", Vector3? Around = null);
}
