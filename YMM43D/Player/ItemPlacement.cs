using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class ItemPlacement
    {
        public static Matrix4x4 GetWorldMatrix(
            IVideoItem item,
            in FrameContext time,
            Matrix4x4 cameraMatrix)
        {
            var zoom = Matrix4x4.CreateScale(item.Zoom.GetFloat(time) / 100f);

            var rotation = Matrix4x4.CreateRotationZ(-Rotation3D.ToRadians(item.Rotation.GetFloat(time)));

            var translation = Matrix4x4.CreateTranslation(
                WorldScale.ToWorld(item.X.GetFloat(time)),
                -WorldScale.ToWorld(item.Y.GetFloat(time)),
                WorldScale.ToWorld(item.Z.GetFloat(time)));

            if (cameraMatrix == Matrix4x4.Identity)
                return zoom * rotation * translation;

            return zoom * rotation * WorldScale.ToYUpMatrix(cameraMatrix) * translation;
        }

        public static ScreenPlacement GetScreenPlacement(IVideoItem item, in FrameContext time)
        {
            var zoom = item.Zoom.GetFloat(time) / 100f;

            return new ScreenPlacement(
                new Vector2(item.X.GetFloat(time), item.Y.GetFloat(time)),
                float.IsFinite(zoom) && zoom > 0f ? zoom : 1f,
                item.Rotation.GetFloat(time),
                item.Z.GetFloat(time));
        }

        public static float GetOpacity(IVideoItem item, in FrameContext time)
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

        public static bool IsAliveAt(IVideoItem item, int frame)
            => frame >= item.Frame && frame < item.Frame + item.Length;
    }
}
