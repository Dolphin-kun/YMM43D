using System.Numerics;

namespace YMM43D.Commons
{
    public interface I3DSizeProvider
    {
        bool TryGetSize(out Vector2 size, out Vector2 offset);

        bool ScalesToInputSize => true;
    }
}
