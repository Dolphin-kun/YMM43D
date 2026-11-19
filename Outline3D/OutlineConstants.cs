using System.Numerics;
using System.Runtime.InteropServices;

namespace Outline3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct OutlineConstants
    {
        public Vector4 Region;
        public Vector4 Target;
        public Vector4 Inverse;
        public Vector4 Placement;
        public Vector4 Stroke;
        public Vector4 Shape;
        public Vector4 StrokeColor;
        public Vector4 BrushArea;
        public Vector4 DepthMap;
    }
}
