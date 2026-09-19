using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Player;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Brush;

namespace Outline3D
{
    internal sealed class Outline3DProcessor(Outline3DEffect effect, IGraphicsDevicesAndContext devices)
        : VideoEffect3DProcessorBase(effect, devices)
    {
        private static readonly WorldBounds UnitPlane = new(new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f));

        private readonly Outline3DEffect effect = effect;
        private readonly DeviceResourceCache<OutlinePass> passes = new(device => new OutlinePass(device));
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> planes = new(
            device => new RenderPipeline<TransformConstants>(device, new PlaneMesh(device), new TextureMaterial(device)));

        private readonly D2DTextureBridge brushBridge = new();

        private I3DProvider? solidSource;
        private VideoEffect3DProcessorBase? precedingEffect;

        private IBrushParameter? brushParameter;
        private IBrushSource? brushSource;
        private ID2D1CommandList? brushImage;
        private DeviceLease? brushLease;
        private ID3D11ShaderResourceView? brushTexture;
        private nint brushDeviceKey;
        private Vector2 brushSize;

        public override bool ScalesToInputSize
            => precedingEffect is { } preceding ? preceding.ScalesToInputSize : solidSource is null;

        public override bool TryGetSize(out Vector2 size, out Vector2 offset)
            => precedingEffect is { } preceding
                ? preceding.TryGetSize(out size, out offset)
                : base.TryGetSize(out size, out offset);

        protected override void OnUpdating(EffectDescription effectDescription)
        {
            precedingEffect = null;
            solidSource = null;

            if (SceneDepthCollector.FindOwner(effectDescription) is not { } owner)
                return;

            if (FindPreceding(owner, effectDescription.InputIndex) is { } preceding)
            {
                precedingEffect = preceding as VideoEffect3DProcessorBase;
                solidSource = preceding;
            }
            else
            {
                solidSource = SceneDepthCollector.FindSources(owner, Devices).FirstOrDefault(source => !ReferenceEquals(source, this));
            }

            UpdateBrush(effectDescription);
        }

        private I3DProvider? FindPreceding(YukkuriMovieMaker.Project.Items.IVideoItem owner, int inputIndex)
        {
            var effects = owner.VideoEffects;

            if (effects is null)
                return null;

            I3DProvider? found = null;

            foreach (var candidate in effects)
            {
                if (ReferenceEquals(candidate, effect))
                    return found;

                if (candidate.IsEnabled && candidate is VideoEffect3DBase solid)
                    found = solid.GetInstance(inputIndex, Devices);
            }

            return found;
        }

        protected override bool TryInheritTransform(out Matrix4x4 local, out Matrix4x4 world)
        {
            if (precedingEffect is { } preceding
                && preceding.TryGetLocalMatrix(out local)
                && preceding.TryGetPlacement(out world))
            {
                return true;
            }

            return base.TryInheritTransform(out local, out world);
        }

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (effect.IsOutlineOnly && (render.IsShadowPass || item.DepthOnly))
                return;

            if (render.IsShadowPass || render.IsCapturingScene || item.DepthOnly || !passes.TryGet(render.Device, out var pass))
            {
                DrawSource(render, item);
                return;
            }

            var time = EffectDescription is { } description ? FrameContext.FromItem(description) : item.Time;

            if (GetSourceBounds(time) is { IsEmpty: false } bounds)
                pass.Draw(render, item, bounds, GetLook(time, render.Device), DrawSource);

            if (!effect.IsOutlineOnly)
                DrawSource(render, item);
        }

        private void DrawSource(in Render3DContext render, DrawContext3D item)
        {
            if (solidSource is { } source)
            {
                source.Draw(render, new DrawContext3D
                {
                    World = item.World,
                    Opacity = item.Opacity,
                    Blend = item.Blend,
                    IsAlwaysOnTop = item.IsAlwaysOnTop,
                    DepthOnly = item.DepthOnly,
                    Time = item.Time,
                });

                return;
            }

            var texture = item.Texture ?? GetTexture(render.Device);

            if (texture is null || !planes.TryGet(render.Device, out var pipeline))
                return;

            pipeline.Draw(
                render.Context,
                render.CreateConstants(item.World, item),
                item.ToDrawSettings(FaceCulling.None, texture));
        }

        private OutlineLook GetLook(in FrameContext time, ID3D11Device device)
        {
            var brush = effect.StrokeBrush;
            var solid = brush.IsSolidColorBrushParameter();
            var color = solid ? brush.GetSolidColorBrushColor() : System.Windows.Media.Colors.White;
            var opacity = Math.Clamp(effect.Opacity.GetFloat(time) / 100f, 0f, 1f);

            return new OutlineLook(
                MathF.Max(effect.StrokeThickness.GetFloat(time), 0f),
                MathF.Max(effect.Blur.GetFloat(time), 0f),
                effect.Quality.GetFloat(time),
                Math.Clamp(effect.Smoothness.GetFloat(time), 0f, 100f),
                effect.IsOutlineOnly,
                effect.IsAngular,
                new Vector2(effect.X.GetFloat(time), effect.Y.GetFloat(time)),
                MathF.Max(effect.Zoom.GetFloat(time) / 100f, 0f),
                effect.Rotation.GetFloat(time),
                new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f * opacity),
                solid ? null : GetBrushTexture(device),
                brushSize);
        }

        private WorldBounds GetSourceBounds(in FrameContext time)
            => solidSource switch
            {
                I3DBounds bounds => bounds.GetLocalBounds(time),
                null => UnitPlane,
                _ => WorldBounds.Empty,
            };

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            var bounds = GetSourceBounds(itemTime);

            if (bounds.IsEmpty)
                return bounds;

            var zoom = MathF.Max(effect.Zoom.GetFloat(itemTime) / 100f, 1f);
            var spread = effect.Rotation.GetFloat(itemTime) % 360f == 0f ? zoom : zoom * MathF.Sqrt(2f);

            var reach = WorldScale.ToWorld(
                MathF.Max(effect.StrokeThickness.GetFloat(itemTime), 0f)
                + MathF.Max(effect.Blur.GetFloat(itemTime), 0f) * 2f
                + MathF.Max(MathF.Abs(effect.X.GetFloat(itemTime)), MathF.Abs(effect.Y.GetFloat(itemTime)))
                + 2f);

            var scale = TryGetLocalMatrix(out var local)
                ? new Vector3(
                    new Vector3(local.M11, local.M12, local.M13).Length(),
                    new Vector3(local.M21, local.M22, local.M23).Length(),
                    new Vector3(local.M31, local.M32, local.M33).Length())
                : Vector3.One;

            var margin = new Vector3(reach) / Vector3.Max(scale, new Vector3(1e-4f));
            var center = bounds.Center;
            var half = (bounds.Max - bounds.Min) / 2f * spread + margin;

            return new WorldBounds(center - half, center + half);
        }

        private void UpdateBrush(EffectDescription description)
        {
            var brush = effect.StrokeBrush;

            if (brush.IsSolidColorBrushParameter())
            {
                ReleaseBrush();
                return;
            }

            var time = FrameContext.FromItem(description);
            var bounds = GetSourceBounds(time);
            var itemPixels = WorldScale.ToPixels(1f);

            var extent = solidSource is null || (precedingEffect?.ScalesToInputSize ?? false)
                ? (TryGetSize(out var size, out _) ? size : Vector2.One)
                : new Vector2(bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y) * itemPixels;

            var pad = (MathF.Max(effect.StrokeThickness.GetFloat(time), 0f) + MathF.Max(effect.Blur.GetFloat(time), 0f) * 2f) * 2f;
            brushSize = Vector2.Max(extent + new Vector2(pad), Vector2.One);

            if (!ReferenceEquals(brushParameter, brush.Parameter))
            {
                brushSource?.Dispose();
                brushSource = null;
                brushParameter = brush.Parameter;
            }

            brushSource ??= brush.CreateBrush(Devices);

            var rect = new Rect(-brushSize.X / 2f, -brushSize.Y / 2f, brushSize.X, brushSize.Y);

            lock (D2DGate.Sync)
            {
                brushSource.Update(new BrushSourceDescription(description, rect));

                var context = Devices.DeviceContext;
                var previous = context.Target;

                brushImage?.Dispose();
                brushImage = context.CreateCommandList();

                context.Target = brushImage;
                context.BeginDraw();
                context.Clear(null);
                context.FillRectangle(rect, brushSource.Brush);
                context.EndDraw();
                context.Target = previous;
                previous?.Dispose();

                brushImage.Close();
            }

            var device = (brushLease ??= GraphicsDevicePool.Acquire()).Device;

            brushTexture = brushBridge.GetTexture(device, Devices, brushImage, this, out _);
            brushDeviceKey = brushTexture is null ? nint.Zero : device.NativePointer;
        }

        private ID3D11ShaderResourceView? GetBrushTexture(ID3D11Device device)
            => brushTexture is not null && brushDeviceKey == device.NativePointer ? brushTexture : null;

        private void ReleaseBrush()
        {
            brushTexture = null;
            brushDeviceKey = nint.Zero;

            brushBridge.Clear();

            brushSource?.Dispose();
            brushSource = null;
            brushParameter = null;

            lock (D2DGate.Sync)
            {
                brushImage?.Dispose();
                brushImage = null;
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            ReleaseBrush();
            brushBridge.Dispose();

            brushLease?.Dispose();
            brushLease = null;

            passes.Dispose();
            planes.Dispose();
        }
    }
}
