using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public interface IGraphics3D : IDisposable
    {
        public I3DGeometry Geometry { get; }
        public I3DMaterial Material { get; }
        public ID3D11InputLayout InputLayout { get; }
    }
}
