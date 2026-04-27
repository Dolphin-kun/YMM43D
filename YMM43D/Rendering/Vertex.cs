using System.Numerics;
using Vortice.Mathematics;

namespace YMM43D.Rendering
{
    public struct Vertex(Vector3 position, Color4 color, Vector2 texCoord)
    {
        public Vector3 Position = position;
        public Color4 Color = color;
        public Vector2 TexCoord = texCoord;
    }
}
