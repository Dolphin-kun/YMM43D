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

        /// <summary>
        /// 実寸はワールド行列ではなく、格子の大きさとして自分で扱います。
        /// </summary>
        /// <remarks>
        /// 取り込ませると、格子が縦横で違う倍率に引き伸ばされます。粒が長方形になり、
        /// 線の太さも向きによって変わってしまいます。
        /// </remarks>
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

            if (effect.DrawFaces && grid.Faces is { } faces)
            {
                // 面だけに掛かる不透明度とそのばらつき。他の形状には効かせない。
                var faceConstants = constants with
                {
                    ExtraOpacity = Math.Clamp(effect.FaceOpacity.GetFloat(time) / 100f, 0f, 1f),
                    OpacityRandomness = Math.Clamp(
                        effect.FaceOpacityRandomness.GetFloat(time) / 100f, 0f, 1f),
                };

                pipeline.Draw(render.Context, faceConstants, settings, faces);
            }

            if (effect.DrawLines && grid.Lines is { } lines && constants.LineHalfWidth > 0f)
                pipeline.Draw(render.Context, constants, settings, lines);

            if (effect.DrawPoints && constants.PointHalfSize > 0f)
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
                ViewRight = GetViewAxis(render, world, Vector3.UnitX),
                ViewUp = GetViewAxis(render, world, Vector3.UnitY),
                ViewForward = GetViewAxis(render, world, -Vector3.UnitZ),
                PointHalfSize = WorldScale.ToWorld(effect.PointSize.GetFloat(time)) / 2f,
                LineHalfWidth = WorldScale.ToWorld(effect.LineWidth.GetFloat(time)) / 2f,
                ExtraOpacity = 1f,
                OpacityRandomness = 0f,
            };
        }

        /// <summary>
        /// カメラの向きを、この形状のローカル座標系に持ち込みます。
        /// </summary>
        /// <remarks>
        /// 粒と線を画面に正対させるのに使います。逆行列が取れない場合（大きさが 0 など）は、
        /// 正対をあきらめて素直な軸を返します。
        /// </remarks>
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

            // 平行移動を除いた向きだけを持ち込む。
            var worldAxis = Vector3.TransformNormal(viewAxis, inverseView);
            var localAxis = Vector3.TransformNormal(worldAxis, inverseWorld);

            return localAxis.LengthSquared() > 0f ? Vector3.Normalize(localAxis) : viewAxis;
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

        /// <summary>
        /// 点群そのものに掛ける、大きさ・回転・位置。
        /// </summary>
        /// <param name="centerPixels">
        /// 入力画像の中心が、アイテムの原点からどれだけずれているか（ピクセル、Y は下が正）。
        /// 実寸をワールド行列に取り込んでいないぶん、この寄せは自分で行います。
        /// </param>
        /// <remarks>
        /// 大きさと回転は点群自身の中心に掛かり、そのあと画像の中心へ寄せてから
        /// 指定された位置へ動かします。
        /// </remarks>
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

            // ばらつきと、粒や線の太さのぶん、格子より一回り広がる。
            var thickness = MathF.Max(
                effect.PointSize.GetFloat(itemTime),
                effect.LineWidth.GetFloat(itemTime));

            var margin = new Vector3(
                WorldScale.ToWorld(effect.ScatterX.GetFloat(itemTime)),
                WorldScale.ToWorld(effect.ScatterY.GetFloat(itemTime)),
                WorldScale.ToWorld(effect.ScatterZ.GetFloat(itemTime)))
                + new Vector3(WorldScale.ToWorld(thickness) / 2f);

            var half = extent / 2f + margin;

            return new WorldBounds(-half, half)
                .Transform(GetLocalMatrix(itemTime, offsetPixels + sizePixels / 2f));
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
