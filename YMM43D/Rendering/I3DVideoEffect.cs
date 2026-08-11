using System.Numerics;
using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    /// <summary>
    /// VideoEffect 向けの 3D 描画インターフェース。
    /// I3DProvider（3D描画）と I3DTextureProvider（テクスチャ提供）を統合し、
    /// VideoEffectBase を継承するエフェクトが 3D 機能を提供することを示す。
    /// </summary>
    public interface I3DVideoEffect : I3DProvider, I3DTextureProvider
    {
    }
}
