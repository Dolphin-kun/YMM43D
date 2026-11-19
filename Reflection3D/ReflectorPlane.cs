using System.Numerics;

namespace Reflection3D
{
    internal readonly record struct ReflectorPlane(Vector3 Normal, Vector3 Centroid, float Area);
}
