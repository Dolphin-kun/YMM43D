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
        /// <summary>
        /// 一辺 1 の立方体の外接球の直径。どの向きに回転しても収まる大きさです。
        /// </summary>
        private static readonly float DiagonalRatio = MathF.Sqrt(3f);

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
            var rotation = Rotation3D.ForObject(
                parameter.RotationX.GetFloat(item.Time),
                parameter.RotationY.GetFloat(item.Time),
                parameter.RotationZ.GetFloat(item.Time));

            var world = Matrix4x4.CreateScale(GetEdgeLength(item.Time)) * rotation * item.World;
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

        protected override float GetWorldExtent(in FrameContext itemTime)
            => GetEdgeLength(itemTime) * DiagonalRatio;

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
