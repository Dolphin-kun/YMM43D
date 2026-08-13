using System.Numerics;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Plugin;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace PixelPoints3D
{
    internal sealed class PixelPoints3DProcessor(PixelPoints3DEffect effect, IGraphicsDevicesAndContext devices) : VideoEffect3DProcessorBase(effect, devices)
    {
        private const int MaxPoints = 300_000;

        private readonly PixelPoints3DEffect effect = effect;
        private readonly DeviceResourceCache<GridResources> resources = new(device => new GridResources(device));

        public override bool RequiresMappedTexture => true;

        public override bool ScalesToInputSize => false;

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            if (!TryGetSize(out var sizePixels, out var offsetPixels))
                return;

            var extent = GetExtent(time, sizePixels);
            if (extent.X <= 0 || extent.Y <= 0)
                return;

            var size = GetGridSize(time, sizePixels);
            var world = GetLocalMatrix(time, offsetPixels + sizePixels / 2f) * item.World;

            var shared = resources.Get(render.Device);
            var grid = shared.GetGrid(size);
            var pipeline = shared.Pipeline;

            var constants = BuildConstants(time, item, render, world, size, extent);
            var settings = item.ToDrawSettings(FaceCulling.None, texture);

            if (effect.Face is { } face && grid.Faces is { } faces)
            {
                var faceConstants = WithColor(constants, face.Color, face.ColorSource) with
                {
                    ExtraOpacity = Math.Clamp(face.Opacity.GetFloat(time) / 100f, 0f, 1f),
                    OpacityRandomness = Math.Clamp(
                        face.OpacityRandomness.GetFloat(time) / 100f, 0f, 1f),
                };

                pipeline.Draw(render.Context, faceConstants, settings, faces);
            }

            if (effect.Line is { } line && grid.Lines is { } lines && constants.LineHalfWidth > 0f)
            {
                var lineConstants = WithColor(constants, line.Color, line.ColorSource) with
                {
                    LineRandomness = Math.Clamp(line.Randomness.GetFloat(time) / 100f, 0f, 1f),
                };

                pipeline.Draw(render.Context, lineConstants, settings, lines);
            }

            if (effect.Point is { } point && constants.PointHalfSize > 0f)
            {
                var pointConstants = WithColor(constants, point.Color, point.ColorSource) with
                {
                    PointIsRound = point.Shape == PointShape.Circle ? 1f : 0f,
                };

                pipeline.Draw(render.Context, pointConstants, settings, grid.Points);
            }
        }

        private static PointCloudConstants WithColor(
            in PointCloudConstants constants, Color color, PointColorSource source) => constants with
            {
                Color = new Vector4(color.R, color.G, color.B, color.A) / 255f,
                UseSourceColor = source == PointColorSource.Image ? 1f : 0f,
            };

        private PointCloudConstants BuildConstants(
            in FrameContext time,
            DrawContext3D item,
            in Render3DContext render,
            in Matrix4x4 world,
            GridSize size,
            Vector3 extent)
        {
            var deform = PointDeform.Create(effect, time, extent);

            return new PointCloudConstants
            {
                DeformAxis = deform.Axis,
                DeformKind = (float)deform.Kind,
                DeformAmount = deform.Amount,
                DeformPeriod = deform.Period,
                DeformPhase = deform.Phase,

                WorldViewProjection = Matrix4x4.Transpose(render.GetWorldViewProjection(world)),
                GridCount = new Vector3(size.X, size.Y, size.Z),
                Threshold = Math.Clamp(effect.Threshold.GetFloat(time) / 100f, 0f, 1f),
                Extent = extent,
                Opacity = item.Opacity,
                Scatter = new Vector3(
                    WorldScale.ToWorld(effect.ScatterX.GetFloat(time)),
                    WorldScale.ToWorld(effect.ScatterY.GetFloat(time)),
                    WorldScale.ToWorld(effect.ScatterZ.GetFloat(time))),
                Seed = effect.Seed.GetFloat(time),
                ViewRight = GetViewAxis(render, world, Vector3.UnitX),
                ViewUp = GetViewAxis(render, world, Vector3.UnitY),
                ViewForward = GetViewAxis(render, world, -Vector3.UnitZ),
                PointHalfSize = GetThickness(effect.Point?.Size, time) / 2f,
                LineHalfWidth = GetThickness(effect.Line?.Width, time) / 2f,
                ExtraOpacity = 1f,
                OpacityRandomness = 0f,
            };
        }

        private static float GetThickness(Animation? pixels, in FrameContext time)
            => pixels is null ? 0f : WorldScale.ToWorld(pixels.GetFloat(time));

        private static Vector3 GetViewAxis(
            in Render3DContext render,
            in Matrix4x4 world,
            Vector3 viewAxis)
        {
            if (!Matrix4x4.Invert(render.View, out var inverseView)
                || !Matrix4x4.Invert(world, out var inverseWorld))
            {
                return viewAxis;
            }

            var worldAxis = Vector3.TransformNormal(viewAxis, inverseView);
            var localAxis = Vector3.TransformNormal(worldAxis, inverseWorld);

            return localAxis.LengthSquared() > 0f ? Vector3.Normalize(localAxis) : viewAxis;
        }

        private Vector3 GetExtent(in FrameContext time, Vector2 sizePixels) => new(
            WorldScale.ToWorld(sizePixels.X),
            WorldScale.ToWorld(sizePixels.Y),
            WorldScale.ToWorld(effect.Depth.GetFloat(time)));

        private GridSize GetGridSize(in FrameContext time, Vector2 sizePixels) => GridSize.Create(
            new Vector3(sizePixels.X, sizePixels.Y, effect.Depth.GetFloat(time)),
            new Vector3(
                effect.SpacingX.GetFloat(time),
                effect.SpacingY.GetFloat(time),
                effect.SpacingZ.GetFloat(time)),
            MaxPoints);

        private Matrix4x4 GetLocalMatrix(in FrameContext time, Vector2 centerPixels)
            => Matrix4x4.CreateScale(effect.Scale.GetFloat(time) / 100f)
             * Rotation3D.ForObject(
                   effect.RotationX.GetFloat(time),
                   effect.RotationY.GetFloat(time),
                   effect.RotationZ.GetFloat(time))
             * Matrix4x4.CreateTranslation(
                   WorldScale.ToWorld(effect.PositionX.GetFloat(time) + centerPixels.X),
                   -WorldScale.ToWorld(effect.PositionY.GetFloat(time) + centerPixels.Y),
                   WorldScale.ToWorld(effect.PositionZ.GetFloat(time)));

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            if (!TryGetSize(out var sizePixels, out var offsetPixels))
                return WorldBounds.Empty;

            var extent = GetExtent(itemTime, sizePixels);

            var thickness = MathF.Max(
                GetThickness(effect.Point?.Size, itemTime),
                GetThickness(effect.Line?.Width, itemTime));

            var margin = Vector3.Abs(new Vector3(
                WorldScale.ToWorld(effect.ScatterX.GetFloat(itemTime)),
                WorldScale.ToWorld(effect.ScatterY.GetFloat(itemTime)),
                WorldScale.ToWorld(effect.ScatterZ.GetFloat(itemTime))))
                + new Vector3(thickness / 2f);

            var half = PointDeform.Create(effect, itemTime, extent).Expand(extent / 2f) + margin;

            return new WorldBounds(-half, half)
                .Transform(GetLocalMatrix(itemTime, offsetPixels + sizePixels / 2f));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private sealed class GridResources(ID3D11Device device) : IDisposable
        {
            private PointGrid? grid;

            public RenderPipeline<PointCloudConstants> Pipeline { get; } = new(
                device, GridVertex.InputElements, new PointCloudMaterial(device));

            public PointGrid GetGrid(GridSize size)
            {
                if (grid is { } existing && existing.Size == size)
                    return existing;

                grid?.Dispose();
                return grid = new PointGrid(device, size);
            }

            public void Dispose()
            {
                grid?.Dispose();
                grid = null;
                Pipeline.Dispose();
            }
        }
    }
}
