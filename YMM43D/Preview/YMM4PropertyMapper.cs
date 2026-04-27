using System;
using System.Numerics;
using YMM43D.Rendering;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Preview
{
    public static class YMM4PropertyMapper
    {
        public static DrawContext3D Map(IVideoItem item, int frame, int length, int fps)
        {
            float opacity = (float)(item.Opacity.GetValue(frame, length, fps) / 100.0);
            
            // YMM4の FadeIn / FadeOut は秒単位であるためフレーム単位に変換
            double fadeInFrames = item.FadeIn * fps;
            double fadeOutFrames = item.FadeOut * fps;

            if (frame < fadeInFrames && fadeInFrames > 0) 
                opacity *= (float)(frame / fadeInFrames);
            if (frame > (length - fadeOutFrames) && fadeOutFrames > 0) 
                opacity *= (float)((length - frame) / fadeOutFrames);

            float x = (float)(item.X.GetValue(frame, length, fps) / 100.0);
            float y = (float)(-item.Y.GetValue(frame, length, fps) / 100.0);
            float z = 0;
            try { z = (float)(item.Z.GetValue(frame, length, fps) / 100.0); } catch { }

            var scale = Matrix4x4.CreateScale((float)(item.Zoom.GetValue(frame, length, fps) / 100.0));
            var rotation2D = Matrix4x4.CreateRotationZ((float)(item.Rotation.GetValue(frame, length, fps) * Math.PI / 180.0));
            var world = scale * rotation2D * Matrix4x4.CreateTranslation(x, y, z);

            return new DrawContext3D
            {
                World = world,
                Opacity = Math.Clamp(opacity, 0, 1),
                Blend = item.Blend,
                IsInverted = item.IsInverted,
                IsAlwaysOnTop = item.IsAlwaysOnTop,
                IsZOrderEnabled = item.IsZOrderEnabled,
                IsClippingWithObjectAbove = item.IsClippingWithObjectAbove,
                Frame = frame,
                Length = length,
                FPS = fps
            };
        }
    }
}
