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

        private const float MinViewDistance = 0.01f;

        private const float MaxTangent = 64f;

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

            var tangentToImage = ImageProjection.TangentToImage(
                pixelsPerTangent,
                hostAppliesPlacement ? scene.OwnerScreenPlacement : ScreenPlacement.None);

            var area = GetRenderArea(bounds, placedWorld, view, tangentToImage);
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

        private static RenderArea? GetRenderArea(
            in WorldBounds bounds,
            in Matrix4x4 world,
            in Matrix4x4 view,
            in Matrix3x2 tangentToImage)
        {
            if (bounds.IsEmpty)
                return null;

            var worldView = world * view;

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            foreach (var corner in bounds.GetCorners())
            {
                var viewSpace = Vector3.Transform(corner, worldView);

                var depth = MathF.Max(-viewSpace.Z, MinViewDistance);
                var tangent = new Vector2(viewSpace.X / depth, viewSpace.Y / depth);

                if (!IsUsable(tangent))
                    return null;

                tangent = Vector2.Clamp(tangent, new Vector2(-MaxTangent), new Vector2(MaxTangent));

                var image = Vector2.Transform(tangent, tangentToImage);
                if (!IsUsable(image))
                    return null;

                min = Vector2.Min(min, image);
                max = Vector2.Max(max, image);
            }

            var width = (int)MathF.Ceiling(max.X - min.X);
            var height = (int)MathF.Ceiling(max.Y - min.Y);

            if (width <= 0 || height <= 0)
                return null;

            if (width > MaxRenderSize)
            {
                min.X += (width - MaxRenderSize) / 2f;
                width = MaxRenderSize;
            }

            if (height > MaxRenderSize)
            {
                min.Y += (height - MaxRenderSize) / 2f;
                height = MaxRenderSize;
            }

            return new RenderArea(width, height, min);
        }

        private static bool IsUsable(in Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        public void Dispose() => renderer.Dispose();

        private readonly record struct RenderArea(int Width, int Height, Vector2 Origin);
    }
}
