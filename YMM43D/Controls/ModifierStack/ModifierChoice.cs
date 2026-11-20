using YMM43D.Project.Shape.Modifiers;

namespace YMM43D.Controls.ModifierStack
{
    public sealed record ModifierChoice(ModifierKind Kind, string Name, string Category, IReadOnlyList<string> Keywords);
}
