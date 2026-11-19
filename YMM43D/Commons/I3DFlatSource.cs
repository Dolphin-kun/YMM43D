using System.Numerics;
using Vortice.Direct3D11;

namespace YMM43D.Commons
{
    public interface I3DFlatSource
    {
        bool TryGetFlat(in FrameContext itemTime, ID3D11Device device, out Matrix4x4 local, out ID3D11ShaderResourceView? texture);
    }
}
