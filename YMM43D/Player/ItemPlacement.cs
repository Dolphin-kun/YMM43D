using System.Collections.Immutable;
using System.Numerics;
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

            var rotation = Matrix4x4.CreateRotationZ(-Rotation3D.ToRadians(item.Rotation.GetFloat(time)));

            var translation = Matrix4x4.CreateTranslation(
                WorldScale.ToWorld(item.X.GetFloat(time)),
                -WorldScale.ToWorld(item.Y.GetFloat(time)),
                WorldScale.ToWorld(item.Z.GetFloat(time)));

            return zoom * rotation * translation;
        }

        // YMM4 はアイテムの位置・拡大率・回転を DrawDescription に載せてから
        // エフェクトを通す。登場退場もそこへ足し込まれるので、置き場所は
        // アイテムの値ではなく、通し終えた DrawDescription から作る。
        public static Matrix4x4 GetWorldMatrix(DrawDescription draw)
        {
            var scale = new Vector3(
                (float)draw.Zoom.X,
                (float)draw.Zoom.Y,
                (float)(draw.Zoom.X + draw.Zoom.Y) / 2f);

            var rotation = Rotation3D.ForObject(
                -(float)draw.Rotation.X, -(float)draw.Rotation.Y, -(float)draw.Rotation.Z);

            var translation = Matrix4x4.CreateTranslation(
                WorldScale.ToWorld((float)draw.Draw.X),
                -WorldScale.ToWorld((float)draw.Draw.Y),
                WorldScale.ToWorld((float)draw.Draw.Z));

            return Matrix4x4.CreateScale(scale) * rotation * translation;
        }

        // エフェクトを通す前の DrawDescription。YMM4 が組み立てるものに合わせる。
        public static DrawDescription ToDrawDescription(IVideoItem item, in FrameContext time)
        {
            var zoom = item.Zoom.GetFloat(time) / 100f;

            return new DrawDescription(
                Draw: new Vector3(
                    item.X.GetFloat(time), item.Y.GetFloat(time), item.Z.GetFloat(time)),
                CenterPoint: Vector2.Zero,
                Zoom: new Vector2(zoom, zoom),
                Rotation: new Vector3(0f, 0f, item.Rotation.GetFloat(time)),
                Camera: Matrix4x4.Identity,
                ZoomInterpolationMode: Vortice.Direct2D1.InterpolationMode.Linear,
                Opacity: GetOpacity(item, time),
                Invert: item.IsInverted,
                Controllers: ImmutableList<VideoEffectController>.Empty);
        }

        public static Matrix4x4 WithCamera(in Matrix4x4 local, in Matrix4x4 cameraMatrix)
            => cameraMatrix == Matrix4x4.Identity
                ? local
                : local * WorldScale.ToYUpMatrix(cameraMatrix);

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
