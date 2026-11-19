using System.Numerics;
using Vortice.Mathematics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.Commons
{
    public interface I3DGeometrySource
    {
        bool TryGetGeometry(in FrameContext itemTime, out SurfaceGeometry geometry, out IReadOnlyList<Color4> colors, out Matrix4x4 local);
    }
}
