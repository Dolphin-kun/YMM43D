using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class ItemPlacement
    {
        public static Matrix4x4 GetWorldMatrix(IVideoItem item, in FrameContext time)
        {
            var zoom = Matrix4x4.CreateScale(item.Zoom.GetFloat(time) / 100f);
            var rotation = Matrix4x4.CreateRotationZ(-float.DegreesToRadians(item.Rotation.GetFloat(time)));
            var translation = Matrix4x4.CreateTranslation(WorldScale.ToWorldPosition(item.X.GetFloat(time), item.Y.GetFloat(time), item.Z.GetFloat(time)));

            return zoom * rotation * translation;
        }

        public static Matrix4x4 GetWorldMatrix(DrawDescription draw)
        {
            var scale = new Vector3(draw.Zoom.X, draw.Zoom.Y, (draw.Zoom.X + draw.Zoom.Y) / 2f);
            var rotation = Rotation3D.ForObject(-draw.Rotation.X, -draw.Rotation.Y, -draw.Rotation.Z);
            var translation = Matrix4x4.CreateTranslation(WorldScale.ToWorldPosition(draw.Draw.X, draw.Draw.Y, draw.Draw.Z));

            return GetCenterMatrix(draw) * Matrix4x4.CreateScale(scale) * rotation * translation;
        }

        public static DrawDescription ToDrawDescription(IVideoItem item, in FrameContext time)
        {
            var zoom = item.Zoom.GetFloat(time) / 100f;

            return new DrawDescription(
                Draw: new Vector3(item.X.GetFloat(time), item.Y.GetFloat(time), item.Z.GetFloat(time)),
                CenterPoint: Vector2.Zero,
                Zoom: new Vector2(zoom, zoom),
                Rotation: new Vector3(0f, 0f, item.Rotation.GetFloat(time)),
                Camera: Matrix4x4.Identity,
                ZoomInterpolationMode: InterpolationMode.Linear,
                Opacity: GetOpacity(item, time),
                Invert: item.IsInverted,
                Controllers: []);
        }

        public static Matrix4x4 WithCamera(in Matrix4x4 local, in Matrix4x4 cameraMatrix)
            => cameraMatrix == Matrix4x4.Identity ? local : local * WorldScale.ToYUpMatrix(cameraMatrix);

        public static ScreenPlacement GetScreenPlacement(IVideoItem item, in FrameContext time)
        {
            var zoom = item.Zoom.GetFloat(time) / 100f;

            return new ScreenPlacement(
                new Vector2(item.X.GetFloat(time), item.Y.GetFloat(time)),
                float.IsFinite(zoom) && zoom > 0f ? zoom : 1f,
                item.Rotation.GetFloat(time),
                item.Z.GetFloat(time));
        }

        private static Matrix4x4 GetCenterMatrix(DrawDescription draw)
        {
            var center = WorldScale.ToWorldPosition(draw.CenterPoint.X, draw.CenterPoint.Y, 0f);

            return center.LengthSquared() < 1e-12f ? Matrix4x4.Identity : Matrix4x4.CreateTranslation(-center);
        }

        private static float GetOpacity(IVideoItem item, in FrameContext time)
        {
            var opacity = item.Opacity.GetFloat(time) / 100f;

            var fadeInFrames = item.FadeIn * time.Fps;
            if (fadeInFrames > 0 && time.Frame < fadeInFrames)
                opacity *= (float)(time.Frame / fadeInFrames);

            var fadeOutFrames = item.FadeOut * time.Fps;
            if (fadeOutFrames > 0 && time.Frame > time.Length - fadeOutFrames)
                opacity *= (float)((time.Length - time.Frame) / fadeOutFrames);

            return opacity;
        }
    }
}
