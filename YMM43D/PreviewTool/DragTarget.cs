using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal readonly record struct DragTarget(IVideoItem Item, EditScope Scope, FrameContext Time, Matrix4x4 FromWorld);
}
