using System.Numerics;
using Vortice.Mathematics;
using Vortice.Direct2D1;
using YMM43D.Commons;
using YMM43D.Player;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;

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
            out float imageReach,
            I3DProvider? self = null,
            bool hostAppliesPlacement = false,
            Matrix4x4? placement = null)
        {
            imageReach = 0f;

            var itemTime = FrameContext.FromItem(description);

            var camera = SceneCameraResolver.Resolve(description);
            var view = camera.GetPose().ViewMatrix;
            var pixelsPerTangent = SceneProjection.GetPixelsPerTangent(
                camera, description.ScreenSize.Height);

            var scene = SceneDepthCollector.Collect(description, self, devices);
            var lighting = SceneLightingResolver.Resolve(description);

            var placedWorld = world * (placement ?? scene.OwnerPlacement) * scene.OwnerGroupTransform;

            var screenPlacement = (hostAppliesPlacement ? scene.OwnerScreenPlacement : ScreenPlacement.None)
                .Then(scene.OwnerGroupScreen);

            var tangentToImage = ImageProjection.TangentToImage(pixelsPerTangent, screenPlacement);

            var visible = ImageArea.ForScreen(
                new Vector2((float)description.ScreenSize.Width, (float)description.ScreenSize.Height),
                screenPlacement,
                VisibleMargin);

            var area = CoversScreen(scene.Owner)
                ? ScreenArea(description, screenPlacement)
                : RenderArea.Measure(
                    bounds, placedWorld, view, tangentToImage, visible,
                    SceneProjection.NearPlane, MaxRenderSize);

            if (area is not { } target)
            {
                return renderer.RenderEmpty(devices);
            }

            imageReach = scene.OwnerGroupScreen.Depth == 0f ? target.Reach : 0f;

            var item = new DrawContext3D
            {
                World = placedWorld,
                Opacity = 1f,
                Time = itemTime,
            };

            var projection = ImageProjection.Compose(
                tangentToImage, target.Origin, target.Width, target.Height);

            return renderer.Render(
                devices, target.Width, target.Height, view, projection, target.Origin, lighting,
                scene.Casters,
                CoversScreen(scene.Owner) ? new Color4(0f, 0f, 0f, 1f) : new Color4(0f, 0f, 0f, 0f),
                render =>
                {
                    DrawOccluders(render, scene.Occluders);
                    draw(render, item);
                });
        }

        private static bool CoversScreen(IVideoItem? owner) => owner is EffectItem;

        private static RenderArea? ScreenArea(TimelineItemSourceDescription description, in ScreenPlacement screenPlacement)
        {
            var screen = ImageArea.ForScreen(
                new Vector2((float)description.ScreenSize.Width, (float)description.ScreenSize.Height),
                screenPlacement,
                0f);

            var width = Math.Min((int)MathF.Ceiling(screen.Max.X - screen.Min.X), MaxRenderSize);
            var height = Math.Min((int)MathF.Ceiling(screen.Max.Y - screen.Min.Y), MaxRenderSize);

            return width > 0 && height > 0 ? new RenderArea(width, height, screen.Min) : null;
        }

        private static void DrawOccluders(
            in Render3DContext render,
            IReadOnlyList<SceneDepthCollector.Occluder> occluders)
        {
            foreach (var occluder in occluders)
            {
                if (occluder.Provider is I3DBounds bounded
                    && render.IsOutside(bounded.GetLocalBounds(occluder.Time), occluder.World))
                {
                    continue;
                }

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
