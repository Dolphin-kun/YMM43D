using System;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YMM43D.Rendering;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Preview
{
    /// <summary>
    /// IVideoItem のプロパティを DrawContext3D にマッピングするヘルパー
    /// </summary>
    public static class PropertyMapper
    {
        public static DrawContext3D Map(IVideoItem item, int frame, int length, int fps, ID3D11Device device, object? scene, TimelineSourceDescription? timelineSourceDescription, bool requireTexture = true)
        {
            float opacity = (float)(item.Opacity.GetValue(frame, length, fps) / 100.0);
            
            // フェード計算
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

            var scale = Matrix4x4.CreateScale((float)(item.Zoom.GetValue(frame, length, fps) / 100.0));
            var rotation2D = Matrix4x4.CreateRotationZ((float)(item.Rotation.GetValue(frame, length, fps) * Math.PI / 180.0));
            var world = scale * rotation2D * Matrix4x4.CreateTranslation(x, y, z);

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
                FPS = fps
            };

            if (requireTexture)
            {
                // 1. 標準アイテムからのテクスチャ取得
                context.Texture = GetTextureFromItem(item, frame, length, fps, device, scene, timelineSourceDescription);
                context.OwnsTexture = context.Texture != null;

                // 2. エフェクトによるテクスチャ上書き
                if (item.VideoEffects != null)
                {
                    foreach (var effect in item.VideoEffects)
                    {
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
            }

            return context;
        }

        private static ID3D11ShaderResourceView? GetTextureFromItem(IVideoItem item, int frame, int length, int fps, ID3D11Device device, object? scene, TimelineSourceDescription? timelineSourceDescription)
        {
            if (scene == null || timelineSourceDescription == null || SharedGraphics.Devices == null) return null;

            try
            {
                using var source = item.CreateVideoSource(SharedGraphics.Devices, (YukkuriMovieMaker.Project.Scene)scene);
                if (source == null) return null;

                ID2D1Image? image = null;

                // 2. ISource としての処理
                if (source is YukkuriMovieMaker.Player.Video.ISource s)
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

                // 3. IDrawable としての処理 (フォールバック)
                if (image == null)
                {
                    if (source is YukkuriMovieMaker.Player.Video.IDrawable drawable)
                    {
                        image = drawable.Output;
                    }
                }

                if (image != null)
                {
                    return D3D11Helper.CreateSrvFromD2DImage(device, image);
                }
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }
    }
}
