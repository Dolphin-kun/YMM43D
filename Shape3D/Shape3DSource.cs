using System.Numerics;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;

namespace Shape3D
{
    /// <summary>
    /// 3D図形アイテムの描画。3Dプレビューと動画出力の両方から使われます。
    /// </summary>
    internal sealed class Shape3DSource : Shape3DSourceBase
    {
        private readonly Shape3DParameter parameter;
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines;

        public Shape3DSource(IGraphicsDevicesAndContext devices, Shape3DParameter parameter) : base(devices)
        {
            this.parameter = parameter;
            pipelines = new DeviceResourceCache<RenderPipeline<TransformConstants>>(
                device => new RenderPipeline<TransformConstants>(
                    device,
                    BoxMesh.CreateUnitCube(device),
                    new VertexColorMaterial(device)));
        }

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var world = GetLocalMatrix(item.Time) * item.World;
            var constants = TransformConstants.Create(render.GetWorldViewProjection(world), item.Opacity);

            var pipeline = pipelines.Get(render.Device);

            // 半透明でも面の前後関係が正しく見えるよう、内側の面を先に描いてから
            // 外側の面を重ねる。
            pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Front));
            pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Back));
        }

        /// <summary>
        /// 立方体の一辺の長さ（ワールド単位）。
        /// </summary>
        private float GetEdgeLength(in FrameContext itemTime)
            => WorldScale.ToWorld(parameter.Size.GetFloat(itemTime));

        /// <summary>
        /// 大きさと回転を掛けた、この図形だけの変換。
        /// </summary>
        private Matrix4x4 GetLocalMatrix(in FrameContext itemTime)
            => Matrix4x4.CreateScale(GetEdgeLength(itemTime))
             * Rotation3D.ForObject(
                   parameter.RotationX.GetFloat(itemTime),
                   parameter.RotationY.GetFloat(itemTime),
                   parameter.RotationZ.GetFloat(itemTime));

        /// <remarks>
        /// 回転角は分かっているので、実際に回した範囲を返す。どの向きにも対応できる
        /// 外接立方体を返すと、辺の長さが最大で √3 ≒ 1.73 倍になり、そのぶん
        /// 出力画像が無駄に大きくなる。
        /// </remarks>
        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
            => WorldBounds.FromCube(1f).Transform(GetLocalMatrix(itemTime));

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
