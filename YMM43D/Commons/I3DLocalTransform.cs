using System.Numerics;

namespace YMM43D.Commons
{
    public interface I3DLocalTransform
    {
        bool TryGetLocalMatrix(out Matrix4x4 matrix);
    }
}
