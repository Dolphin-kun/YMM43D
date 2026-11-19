using System.Numerics;

namespace YMM43D.Commons
{
    public interface I3DPlacedInstance
    {
        bool TryGetPlacement(out Matrix4x4 world);
    }
}
