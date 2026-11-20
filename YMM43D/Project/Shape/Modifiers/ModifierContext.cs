using System.Numerics;
using YMM43D.Commons;

namespace YMM43D.Project.Shape.Modifiers
{
    internal readonly record struct ModifierContext(FrameContext Time, float UnitsPerPixel, Func<int, BooleanTarget?>? Targets, Matrix4x4[]? Skin);
}
