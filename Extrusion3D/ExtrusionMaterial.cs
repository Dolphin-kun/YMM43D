using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics.Materials;

namespace Extrusion3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtrusionConstants
    {
        public Vector4 SideColor;

        public Vector3 CameraLocalPos;

        public int ExtrusionType;
    }

    internal sealed class ExtrusionMaterial(ID3D11Device device) : ShaderMaterial(
        device, typeof(ExtrusionMaterial).Assembly, "ExtrusionVertex.hlsl", "VSMain", "ExtrusionPixel.hlsl", "main");
}
