using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace YMM43D.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SkinnedVertex(Vertex vertex, ushort bone0, ushort bone1, ushort bone2, ushort bone3, Vector4 weights)
    {
        public Vector3 Position = vertex.Position;
        public Color4 Color = vertex.Color;
        public Vector2 TexCoord = vertex.TexCoord;
        public Vector3 Normal = vertex.Normal;
        public Vector4 Tangent = vertex.Tangent;
        public ushort Bone0 = bone0;
        public ushort Bone1 = bone1;
        public ushort Bone2 = bone2;
        public ushort Bone3 = bone3;
        public Vector4 Weights = weights;

        public static int Stride => Marshal.SizeOf<SkinnedVertex>();

        public static InputElementDescription[] InputElements =>
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
            new("TEXCOORD", 0, Format.R32G32_Float, 28, 0),
            new("NORMAL", 0, Format.R32G32B32_Float, 36, 0),
            new("TANGENT", 0, Format.R32G32B32A32_Float, 48, 0),
            new("BLENDINDICES", 0, Format.R16G16B16A16_UInt, 64, 0),
            new("BLENDWEIGHT", 0, Format.R32G32B32A32_Float, 72, 0),
        ];
    }
}
