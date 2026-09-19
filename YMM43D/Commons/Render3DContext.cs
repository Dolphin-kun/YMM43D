using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Player;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public readonly record struct Render3DContext(
        ID3D11Device Device,
        ID3D11DeviceContext Context,
        Matrix4x4 View,
        Matrix4x4 Projection,
        SceneLighting? Lighting = null)
    {
        public IReadOnlyList<SceneDepthCollector.Occluder> Scene { get; init; } = [];

        public bool IsCapturingScene { get; init; }

        public IGraphicsDevicesAndContext? SourceDevices { get; init; }

        public bool IsShadowPass => Lighting is null;

        public Matrix4x4 ViewProjection => View * Projection;

        public Render3DContext BindLights()
        {
            SceneLightBuffer.Bind(Device, Context, (Lighting ?? SceneLighting.Default).LightBuffer);

            return this;
        }

        public Matrix4x4 GetWorldViewProjection(in Matrix4x4 world) => world * View * Projection;

        // 箱の 8 隅がすべて、描く範囲の上下左右のどれか 1 つの外、またはすべてカメラの後ろにあるとき true。
        // 写るかもしれないときは false。
        public bool IsOutside(in WorldBounds bounds, in Matrix4x4 world)
        {
            if (bounds.IsEmpty)
                return false;

            var transform = GetWorldViewProjection(world);
            Span<Vector3> corners = stackalloc Vector3[WorldBounds.CornerCount];
            bounds.WriteCorners(corners);

            int left = 0, right = 0, bottom = 0, top = 0, behind = 0;

            foreach (var corner in corners)
            {
                var clip = Vector4.Transform(new Vector4(corner, 1f), transform);

                if (clip.X < -clip.W) left++;
                if (clip.X > clip.W) right++;
                if (clip.Y < -clip.W) bottom++;
                if (clip.Y > clip.W) top++;
                if (clip.W <= 0f) behind++;
            }

            const int all = WorldBounds.CornerCount;

            return left == all || right == all || bottom == all || top == all || behind == all;
        }

        public Vector3 GetCameraPosition()
        {
            Matrix4x4.Invert(View, out var inverse);
            return inverse.Translation;
        }

        public TransformConstants CreateConstants(
            in Matrix4x4 world, float opacity, bool unlit = false, float alphaCutoff = 0f, SurfaceGloss gloss = default)
            => SceneConstants.Create(world, View, Projection, opacity, Lighting, unlit, alphaCutoff, gloss);

        public TransformConstants CreateConstants(
            in Matrix4x4 world, DrawContext3D item, bool unlit = false, SurfaceGloss gloss = default)
            => SceneConstants.Create(world, View, Projection, item.Opacity, Lighting, unlit, item.AlphaCutoff, gloss);
    }
}
