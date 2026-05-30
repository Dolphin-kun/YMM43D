using System;
using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Rendering;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;
using YMM43D.Commons;
using Math = System.Math;

namespace YMM43D.Preview
{
    public static class PropertyMapper
    {
        private static readonly Dictionary<IVideoItem, IDisposable> videoSourceCache = new();
        private static readonly object sourceCacheLock = new();

        public static void ClearCache()
        {
            lock (sourceCacheLock)
            {
                foreach (var source in videoSourceCache.Values)
                {
                    source.Dispose();
                }
                videoSourceCache.Clear();
            }
        }

        public static DrawContext3D Map(IVideoItem item, int frame, int length, int fps, ID3D11Device device, object? scene, TimelineSourceDescription? timelineSourceDescription, bool requireTexture = true)
        {
            float opacity = (float)(item.Opacity.GetValue(frame, length, fps) / 100.0);
            
            double fadeInFrames = (item.FadeIn > 0) ? item.FadeIn * fps : 0;
            double fadeOutFrames = (item.FadeOut > 0) ? item.FadeOut * fps : 0;

            if (fadeInFrames > 0 && frame < fadeInFrames) 
                opacity *= (float)(frame / fadeInFrames);
            if (fadeOutFrames > 0 && frame > (length - fadeOutFrames)) 
                opacity *= (float)((length - frame) / fadeOutFrames);

            float x = (float)(item.X.GetValue(frame, length, fps) / 100.0);
            float y = (float)(-item.Y.GetValue(frame, length, fps) / 100.0);
            float z = 0;
            try { z = (float)(item.Z.GetValue(frame, length, fps) / 100.0); } catch { }

            float widthInDips = 100f;
            float heightInDips = 100f;
            bool ownsTexture = false;

            ID3D11ShaderResourceView? texture = null;
            if (requireTexture)
            {
                texture = GetTextureFromItem(item, frame, length, fps, device, scene, timelineSourceDescription, out widthInDips, out heightInDips, out ownsTexture);
            }

            var sizeScale = Matrix4x4.CreateScale(widthInDips / 100.0f, heightInDips / 100.0f, 1f);
            var scale = Matrix4x4.CreateScale((float)(item.Zoom.GetValue(frame, length, fps) / 100.0));
            var rotation2D = Matrix4x4.CreateRotationZ((float)(-item.Rotation.GetValue(frame, length, fps) * Math.PI / 180.0));
            var world = sizeScale * scale * rotation2D * Matrix4x4.CreateTranslation(x, y, z);

            var context = new DrawContext3D
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
                FPS = fps,
                Texture = texture,
                OwnsTexture = ownsTexture
            };

            // 2. エフェクトによるテクスチャ上書き
            if (requireTexture && item.VideoEffects != null)
            {
                foreach (var effect in item.VideoEffects)
                {
                    if (!effect.IsEnabled) continue;
                    if (effect is I3DTextureProvider textureProvider)
                    {
                        var tex = textureProvider.GetTexture(device);
                        if (tex != null)
                        {
                            if (context.OwnsTexture)
                            {
                                context.Texture?.Dispose();
                            }
                            context.Texture = tex;
                            context.OwnsTexture = false;
                            break;
                        }
                    }
                }
            }

            return context;
        }

        private static ID3D11ShaderResourceView? GetTextureFromItem(
            IVideoItem item, 
            int frame, 
            int length, 
            int fps, 
            ID3D11Device device, 
            object? scene, 
            TimelineSourceDescription? timelineSourceDescription,
            out float widthInDips,
            out float heightInDips,
            out bool ownsTexture)
        {
            widthInDips = 100f;
            heightInDips = 100f;
            ownsTexture = false;
            if (scene == null || timelineSourceDescription == null || SharedGraphics.Devices == null) return null;

            try
            {
                IDisposable? source = null;
                lock (sourceCacheLock)
                {
                    if (!videoSourceCache.TryGetValue(item, out source))
                    {
                        source = item.CreateVideoSource(SharedGraphics.Devices, (YukkuriMovieMaker.Project.Scene)scene);
                        if (source != null)
                        {
                            videoSourceCache[item] = source;
                        }
                    }
                }
                if (source == null) return null;

                ID2D1Image? image = null;

                if (source is ISource s)
                {
                    s.Update(new TimelineItemSourceDescription(timelineSourceDescription, frame, length, item.Layer));

                    if (s.Outputs != null)
                    {
                        foreach (var output in s.Outputs)
                        {
                            if (output?.Output is ID2D1Image outputImage)
                            {
                                image = outputImage;
                                break;
                            }
                        }
                    }
                }

                if (image == null && source is IDrawable drawable)
                {
                    image = drawable.Output;
                }

                return D3D11Helper.GetOrCreateSrvFromD2DImage(device, image, item, out widthInDips, out heightInDips, out ownsTexture);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
