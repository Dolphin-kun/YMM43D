using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Scene3D;

namespace YMM43D.Plugin
{
    public readonly record struct Render3DContext(
        ID3D11Device Device,
        ID3D11DeviceContext Context,
        Matrix4x4 View,
        Matrix4x4 Projection,
        SceneLighting? Lighting = null)
    {
        public Matrix4x4 ViewProjection => View * Projection;

        public Matrix4x4 GetWorldViewProjection(in Matrix4x4 world) => world * View * Projection;

        public Vector3 GetCameraPosition()
        {
            Matrix4x4.Invert(View, out var inverse);
            return inverse.Translation;
        }

        public TransformConstants CreateConstants(in Matrix4x4 world, float opacity, bool unlit = false)
            => SceneConstants.Create(world, View, Projection, opacity, Lighting, unlit);
    }
}
