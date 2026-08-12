using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;

namespace PixelPoints3D
{
    /// <summary>
    /// 点群3D の描画処理。
    /// </summary>
    internal sealed class PixelPoints3DProcessor : VideoEffect3DProcessorBase
    {
        /// <summary>
        /// 一度に描く点の数の上限。
        /// </summary>
        /// <remarks>
        /// これを超えると間隔を自動で粗くします。頂点バッファの生成はフレームを
        /// またいで使い回されますが、間隔を小刻みに動かされると作り直しが続くため、
        /// 際限なく増えないようにしておきます。
        /// </remarks>
        private const int MaxPoints = 300_000;

        private readonly PixelPoints3DEffect effect;
        private readonly DeviceResourceCache<GridResources> resources;

        public PixelPoints3DProcessor(PixelPoints3DEffect effect, IGraphicsDevicesAndContext devices)
            : base(effect, devices)
        {
            this.effect = effect;
            resources = new DeviceResourceCache<GridResources>(device => new GridResources(device));
        }

        /// <summary>点を打つ場所を決めるのに、アイテムの画像そのものが要る。</summary>
        public override bool RequiresMappedTexture => true;

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null)
                return;

            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            if (!TryGetSize(out var sizePixels, out _))
                return;

            var extent = GetExtent(time, sizePixels);
            if (extent.X <= 0 || extent.Y <= 0)
                return;

            var size = GetGridSize(time, sizePixels);
            var world = GetLocalMatrix(time) * item.World;

            var shared = resources.Get(render.Device);
            var grid = shared.GetGrid(size);
            var pipeline = shared.Pipeline;

            var constants = BuildConstants(time, item, render, world, size, extent);
            var settings = item.ToDrawSettings(FaceCulling.None, texture);

            if (effect.DrawFaces && grid.Faces is { } faces)
                pipeline.Draw(render.Context, constants, settings, faces);

            if (effect.DrawLines && grid.Lines is { } lines)
                pipeline.Draw(render.Context, constants, settings, lines);

            if (effect.DrawPoints)
                pipeline.Draw(render.Context, constants, settings, grid.Points);
        }

        private PointCloudConstants BuildConstants(
            in FrameContext time,
            DrawContext3D item,
            in Render3DContext render,
            in Matrix4x4 world,
            GridSize size,
            Vector3 extent)
        {
            var color = effect.Color;

            return new PointCloudConstants
            {
                WorldViewProjection = Matrix4x4.Transpose(render.GetWorldViewProjection(world)),
                Color = new Vector4(color.R, color.G, color.B, color.A) / 255f,
                GridCount = new Vector3(size.X, size.Y, size.Z),
                Threshold = Math.Clamp(effect.Threshold.GetFloat(time) / 100f, 0f, 1f),
                Extent = extent,
                Opacity = item.Opacity,
                Scatter = new Vector3(
                    WorldScale.ToWorld(effect.ScatterX.GetFloat(time)),
                    WorldScale.ToWorld(effect.ScatterY.GetFloat(time)),
                    WorldScale.ToWorld(effect.ScatterZ.GetFloat(time))),
                Seed = effect.Seed.GetFloat(time),
                UseSourceColor = effect.UseSourceColor ? 1f : 0f,
                PointRight = GetPointAxis(render, world, Vector3.UnitX, time),
                PointUp = GetPointAxis(render, world, Vector3.UnitY, time),
            };
        }

        /// <summary>
        /// 粒をカメラに正対させるための、ローカル座標系での縦横の向きを求めます。
        /// </summary>
        /// <remarks>
        /// カメラの右方向・上方向をワールド行列の逆で戻します。逆行列が取れない場合
        /// （大きさが 0 など）は、正対をあきらめて素直な軸を使います。
        /// </remarks>
        private Vector3 GetPointAxis(
            in Render3DContext render,
            in Matrix4x4 world,
            Vector3 viewAxis,
            in FrameContext time)
        {
            var half = WorldScale.ToWorld(effect.PointSize.GetFloat(time)) / 2f;
            if (half <= 0)
                return Vector3.Zero;

            if (!Matrix4x4.Invert(render.View, out var inverseView)
                || !Matrix4x4.Invert(world, out var inverseWorld))
            {
                return viewAxis * half;
            }

            // 平行移動を除いた向きだけを持ち込む。
            var worldAxis = Vector3.TransformNormal(viewAxis, inverseView);
            var localAxis = Vector3.TransformNormal(worldAxis, inverseWorld);

            return localAxis.LengthSquared() > 0f
                ? Vector3.Normalize(localAxis) * half
                : viewAxis * half;
        }

        /// <summary>格子が占める大きさ（ワールド単位）。</summary>
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

        /// <summary>点群そのものに掛ける、大きさ・回転・位置。</summary>
        private Matrix4x4 GetLocalMatrix(in FrameContext time)
            => Matrix4x4.CreateScale(effect.Scale.GetFloat(time) / 100f)
             * Rotation3D.ForObject(
                   effect.RotationX.GetFloat(time),
                   effect.RotationY.GetFloat(time),
                   effect.RotationZ.GetFloat(time))
             * Matrix4x4.CreateTranslation(
                   WorldScale.ToWorld(effect.PositionX.GetFloat(time)),
                   -WorldScale.ToWorld(effect.PositionY.GetFloat(time)),
                   WorldScale.ToWorld(effect.PositionZ.GetFloat(time)));

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            if (!TryGetSize(out var sizePixels, out _))
                return WorldBounds.Empty;

            var extent = GetExtent(itemTime, sizePixels);

            // ばらつきと粒の大きさのぶん、格子より一回り広がる。
            var margin = new Vector3(
                WorldScale.ToWorld(effect.ScatterX.GetFloat(itemTime)),
                WorldScale.ToWorld(effect.ScatterY.GetFloat(itemTime)),
                WorldScale.ToWorld(effect.ScatterZ.GetFloat(itemTime)))
                + new Vector3(WorldScale.ToWorld(effect.PointSize.GetFloat(itemTime)) / 2f);

            var half = extent / 2f + margin;

            return new WorldBounds(-half, half).Transform(GetLocalMatrix(itemTime));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// デバイス1つ分の資源。格子は分割数が変わったときだけ作り直します。
        /// </summary>
        private sealed class GridResources(ID3D11Device device) : IDisposable
        {
            private PointGrid? grid;

            /// <remarks>
            /// 形状は都度渡します。粒・線・面で頂点の並びは同じなので、
            /// シェーダーと入力レイアウトは1組で足ります。
            /// </remarks>
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
