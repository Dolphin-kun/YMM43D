
namespace YMM43D.Commons
{
    public interface I3DProvider
    {
        bool RequiresMappedTexture { get; }

        void Draw(in Render3DContext render, DrawContext3D item);
    }
}
