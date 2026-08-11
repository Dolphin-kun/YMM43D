using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
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
    public record class TimelineContext(int Frame, int Length, int Fps);

    public record class RenderEnvironmentContext(
        ID3D11Device Device,
        object? Scene,
        YukkuriMovieMaker.Player.Video.TimelineSourceDescription? TimelineSourceDescription,
        SceneCamera Camera
    );

    public class VideoItem3DMapper : IDisposable
    {
        private readonly Dictionary<IVideoItem, IDisposable> videoSourceCache = new();
        private readonly Dictionary<IVideoItem, CachedItemTexture> textureCache = new();
        private readonly Dictionary<IVideoItem, bool> itemHas3DRotation = new();
        private readonly object sourceCacheLock = new();



        public void ClearCache()
        {
            lock (sourceCacheLock)
            {
                foreach (var source in videoSourceCache.Values)
                {
                    source.Dispose();
                }
                videoSourceCache.Clear();

                foreach (var tex in textureCache.Values)
                {
                    tex.Dispose();
                }
                textureCache.Clear();

                itemHas3DRotation.Clear();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void Dispose()
        {
            ClearCache();
            GC.SuppressFinalize(this);
        }

        private static readonly Dictionary<Type, System.Reflection.PropertyInfo?> drawDescPropCache = new();
        private static readonly Dictionary<Type, System.Reflection.PropertyInfo?> cameraPropCache = new();

        private static Matrix4x4 GetDrawDescriptionCamera(object? source)
        {
            if (source == null) return Matrix4x4.Identity;
            try
            {
                var sourceType = source.GetType();
                
                System.Reflection.PropertyInfo? ddProp;
                lock (drawDescPropCache)
                {
                    if (!drawDescPropCache.TryGetValue(sourceType, out ddProp))
                    {
                        ddProp = sourceType.GetProperty("DrawDescription", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        drawDescPropCache[sourceType] = ddProp;
                    }
                }
                
                if (ddProp == null) return Matrix4x4.Identity;

                var dd = ddProp.GetValue(source);
                if (dd == null) return Matrix4x4.Identity;

                var ddType = dd.GetType();
                System.Reflection.PropertyInfo? camProp;
                lock (cameraPropCache)
                {
                    if (!cameraPropCache.TryGetValue(ddType, out camProp))
                    {
                        camProp = ddType.GetProperty("Camera", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        cameraPropCache[ddType] = camProp;
                    }
                }

                if (camProp == null) return Matrix4x4.Identity;

                var cam = camProp.GetValue(dd);
                if (cam is Matrix4x4 m) return m;
            }
            catch { }
            return Matrix4x4.Identity;
        }

        private static bool Is3DMatrix(Matrix4x4 m)
        {
            const float epsilon = 1e-5f;
            return Math.Abs(m.M13) > epsilon ||
                   Math.Abs(m.M14) > epsilon ||
                   Math.Abs(m.M23) > epsilon ||
                   Math.Abs(m.M24) > epsilon ||
                   Math.Abs(m.M31) > epsilon ||
                   Math.Abs(m.M32) > epsilon ||
                   Math.Abs(m.M33 - 1f) > epsilon ||
                   Math.Abs(m.M34) > epsilon ||
                   Math.Abs(m.M43) > epsilon ||
                   Math.Abs(m.M44 - 1f) > epsilon;
        }

        public DrawContext3D Map(
            IVideoItem item, 
            TimelineContext timelineContext, 
            RenderEnvironmentContext envContext, 
            I3DProvider? targetProvider, 
            bool requireTexture = true)
        {
            float opacity = (float)(item.Opacity.GetValue(timelineContext.Frame, timelineContext.Length, timelineContext.Fps) / 100.0);
            
            double fadeInFrames = (item.FadeIn > 0) ? item.FadeIn * timelineContext.Fps : 0;
            double fadeOutFrames = (item.FadeOut > 0) ? item.FadeOut * timelineContext.Fps : 0;

            if (fadeInFrames > 0 && timelineContext.Frame < fadeInFrames) 
				opacity *= (float)(timelineContext.Frame / fadeInFrames);
            if (fadeOutFrames > 0 && timelineContext.Frame > (timelineContext.Length - fadeOutFrames)) 
				opacity *= (float)((timelineContext.Length - timelineContext.Frame) / fadeOutFrames);

            float widthInDips = 100f;
            float heightInDips = 100f;
            bool ownsTexture = false;

            Matrix4x4 cameraMatrix = Matrix4x4.Identity;
            lock (sourceCacheLock)
            {
                if (videoSourceCache.TryGetValue(item, out var cachedSrc))
                {
                    cameraMatrix = GetDrawDescriptionCamera(cachedSrc);
                }
            }
            ID3D11ShaderResourceView? texture = null;
            if (requireTexture)
            {
                texture = GetTextureFromItem(item, timelineContext, envContext, targetProvider,
                    out widthInDips, out heightInDips, out ownsTexture);
            }


            Matrix4x4 sizeScale;
            if (targetProvider is I3DSizeProvider sizeProvider && sizeProvider.TryGetSize(out float w, out float h, out Vector2 offset))
            {
                widthInDips = w;
                heightInDips = h;
                float cx = offset.X + w / 2.0f;
                float cy = offset.Y + h / 2.0f;
                sizeScale = Matrix4x4.CreateScale(w / 100.0f, h / 100.0f, 1f)
                            * Matrix4x4.CreateTranslation(cx / 100.0f, -cy / 100.0f, 0f);
            }
            else
            {
                sizeScale = Matrix4x4.CreateScale(widthInDips / 100.0f, heightInDips / 100.0f, 1f);
            }

            Matrix4x4 world;
            var scale = Matrix4x4.CreateScale((float)(item.Zoom.GetValue(timelineContext.Frame, timelineContext.Length, timelineContext.Fps) / 100.0));
            var rotation2D = Matrix4x4.CreateRotationZ((float)(-item.Rotation.GetValue(timelineContext.Frame, timelineContext.Length, timelineContext.Fps) * Math.PI / 180.0));

            float x = (float)(item.X.GetValue(timelineContext.Frame, timelineContext.Length, timelineContext.Fps) / 100.0);
            float y = (float)(-item.Y.GetValue(timelineContext.Frame, timelineContext.Length, timelineContext.Fps) / 100.0);
            float z = 0;
            try { z = (float)(item.Z.GetValue(timelineContext.Frame, timelineContext.Length, timelineContext.Fps) / 100.0); } catch { }

            var translation = Matrix4x4.CreateTranslation(x, y, z);

            if (cameraMatrix != Matrix4x4.Identity)
            {
                var converted2D = cameraMatrix;
                converted2D.M12 = -converted2D.M12;
                converted2D.M21 = -converted2D.M21;
                converted2D.M42 = -converted2D.M42;
                converted2D.M41 /= 100.0f;
                converted2D.M42 /= 100.0f;

                world = sizeScale * scale * rotation2D * converted2D * translation;
            }
            else
            {
                world = sizeScale * scale * rotation2D * translation;
            }

            var context = new DrawContext3D
            {
                World = world,
                Opacity = Math.Clamp(opacity, 0, 1),
                Blend = item.Blend,
                IsAlwaysOnTop = item.IsAlwaysOnTop,
                Frame = timelineContext.Frame,
                Length = timelineContext.Length,
                FPS = timelineContext.Fps,
                Texture = requireTexture ? texture : null,
                OwnsTexture = requireTexture && ownsTexture,
                ItemCameraMatrix = cameraMatrix
            };

            if (!requireTexture && ownsTexture)
            {
                texture?.Dispose();
            }

            return context;
        }

        private ID3D11ShaderResourceView? GetTextureFromItem(
            IVideoItem item, 
            TimelineContext timelineContext, 
            RenderEnvironmentContext envContext,
            I3DProvider? targetProvider,
            out float widthInDips,
            out float heightInDips,
            out bool ownsTexture)
        {
            widthInDips = 100f;
            heightInDips = 100f;
            ownsTexture = false;
            if (envContext.Scene == null || envContext.TimelineSourceDescription == null || SharedGraphics.Devices == null) return null;

            try
            {
                IDisposable? source = null;
                lock (sourceCacheLock)
                {
                    if (!videoSourceCache.TryGetValue(item, out source))
                    {
                        source = item.CreateVideoSource(SharedGraphics.Devices, (YukkuriMovieMaker.Project.Scene)envContext.Scene);
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
                    s.Update(new TimelineItemSourceDescription(envContext.TimelineSourceDescription, timelineContext.Frame, timelineContext.Length, item.Layer));

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

                ID3D11ShaderResourceView? imageSrv = null;

                if (targetProvider is I3DTextureProvider textureProvider)
                {
                    var tex = textureProvider.GetTexture(envContext.Device);
                    if (tex != null)
                    {
                        imageSrv = tex;
                        ownsTexture = false;
                    }
                }

                if (imageSrv == null && image != null)
                {
                    imageSrv = D3D11Helper.GetOrCreateSrvFromD2DImage(envContext.Device, image, item, null, textureCache, out widthInDips, out heightInDips, out ownsTexture);
                }
                else if (imageSrv != null && image != null)
                {
                    D3D11Helper.GetOrCreateSrvFromD2DImage(envContext.Device, image, item, null, textureCache, out widthInDips, out heightInDips, out bool dummy);
                }

                return imageSrv;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
