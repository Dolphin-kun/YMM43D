using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool
{
    internal sealed record KnifeStep(Vector3 World, KnifePoint? Hit, List<KnifePoint> Crossings);
}
