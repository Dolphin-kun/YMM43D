using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Scene3D;

namespace YMM43D.Plugin
{
    public interface I3DProvider
    {
        bool RequiresMappedTexture { get; }

        void Draw(in Render3DContext render, DrawContext3D item);
    }

    public interface I3DBounds
    {
        WorldBounds GetLocalBounds(in FrameContext itemTime);
    }

    public interface I3DTextureProvider
    {
        ID3D11ShaderResourceView? GetTexture(ID3D11Device device);
    }

    public interface I3DSizeProvider
    {
        bool TryGetSize(out Vector2 size, out Vector2 offset);

        bool ScalesToInputSize => true;
    }

    public interface I3DLocalTransform
    {
        bool TryGetLocalMatrix(out Matrix4x4 matrix);
    }

    public interface I3DVideoEffect : I3DProvider, I3DTextureProvider;
}
