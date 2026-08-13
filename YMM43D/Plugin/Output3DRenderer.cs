using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Camera;
using YMM43D.Commons;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    public delegate void Draw3DCallback(in Render3DContext render, DrawContext3D item);

    public sealed class Output3DRenderer : IDisposable
    {
        private const int MaxRenderSize = 4096;

        private const float VisibleMargin = 0.5f;

        private readonly Renderer3DTo2D renderer = new();

        public ID2D1Image Render(
            IGraphicsDevicesAndContext devices,
            TimelineItemSourceDescription description,
            WorldBounds bounds,
            Matrix4x4 world,
            Draw3DCallback draw,
            I3DProvider? self = null,
            bool hostAppliesPlacement = false,
            Matrix4x4? placement = null)
        {
            var itemTime = FrameContext.FromItem(description);

            var camera = SceneCameraResolver.Resolve(description);
            var view = camera.GetPose().ViewMatrix;
            var pixelsPerTangent = SceneProjection.GetPixelsPerTangent(
                camera, description.ScreenSize.Height);

            var scene = SceneDepthCollector.Collect(description, self);

            var placedWorld = world * (placement ?? scene.OwnerPlacement);

            var screenPlacement = hostAppliesPlacement ? scene.OwnerScreenPlacement : ScreenPlacement.None;

            var tangentToImage = ImageProjection.TangentToImage(pixelsPerTangent, screenPlacement);

            var visible = ImageArea.ForScreen(
                new Vector2((float)description.ScreenSize.Width, (float)description.ScreenSize.Height),
                screenPlacement,
                VisibleMargin);

            var area = RenderArea.Measure(
                bounds, placedWorld, view, tangentToImage, visible,
                SceneProjection.NearPlane, MaxRenderSize);

            if (area is not { } target)
            {
                return renderer.RenderEmpty(devices);
            }

            var item = new DrawContext3D
            {
                World = placedWorld,
                Opacity = 1f,
                Time = itemTime,
            };

            var projection = ImageProjection.Compose(
                tangentToImage, target.Origin, target.Width, target.Height);

            return renderer.Render(
                devices, target.Width, target.Height, view, projection, target.Origin,
                render =>
                {
                    DrawOccluders(render, scene.Occluders);
                    draw(render, item);
                });
        }

        private static void DrawOccluders(
            in Render3DContext render,
            IReadOnlyList<SceneDepthCollector.Occluder> occluders)
        {
            foreach (var occluder in occluders)
            {
                var context = new DrawContext3D
                {
                    World = occluder.World,
                    Opacity = 1f,
                    Time = occluder.Time,
                    DepthOnly = true,
                };

                try
                {
                    occluder.Provider.Draw(render, context);
                }
                catch
                {
                }
            }
        }

        public void Dispose() => renderer.Dispose();
    }
}
