using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Camera;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;

namespace YMM43D.PreviewTool.Rendering
{
    /// <summary>
    /// 画面の隅に、いまどちらを向いているかの目印を描きます。
    /// </summary>
    /// <remarks>
    /// 回り込んでいるうちに、どちらが手前でどちらが上なのか分からなくなります。
    /// シーンとは別の小さな枠を作り、視点の向きだけを写した軸をそこに描きます。
    /// <para>
    /// 軸の正の側は長い腕の先に小さな多面体を付け、負の側は短い腕だけにします。
    /// 文字を使わずに、腕の長さと先端の有無だけで向きが読めます。
    /// </para>
    /// </remarks>
    internal sealed class AxisIndicatorRenderer : IDisposable
    {
        private static readonly Color4 AxisXColor = new(1f, 0.3f, 0.35f, 1f);
        private static readonly Color4 AxisYColor = new(0.45f, 0.9f, 0.35f, 1f);
        private static readonly Color4 AxisZColor = new(0.35f, 0.6f, 1f, 1f);

        /// <summary>正の側の腕の長さ。</summary>
        private const float PositiveArm = 1f;

        /// <summary>負の側の腕の長さ。短くすることで、どちらが正か分かる。</summary>
        private const float NegativeArm = 0.45f;

        /// <summary>先端に付ける多面体の大きさ。</summary>
        private const float CapRadius = 0.17f;

        /// <summary>枠に収まる範囲。腕と先端が切れない大きさにする。</summary>
        private const float ViewSize = 2.7f;

        /// <summary>目印を写す距離。平行投影なので見え方には影響しない。</summary>
        private const float ViewDistance = 4f;

        /// <summary>枠の一辺を、描画先の短い方の何割にするか。</summary>
        private const float SizeRatio = 0.2f;

        private const float MinSize = 52f;
        private const float MaxSize = 108f;

        /// <summary>枠と画面の端との隙間（ピクセル）。</summary>
        private const float Margin = 10f;

        private readonly DeviceResourceCache<IndicatorResources> resources;

        public AxisIndicatorRenderer()
        {
            resources = new DeviceResourceCache<IndicatorResources>(
                device => new IndicatorResources(device));
        }

        /// <summary>
        /// 目印を描き、描画先の設定を元に戻します。
        /// </summary>
        /// <param name="pose">いまの視点の姿勢。位置は使わず、向きだけを写します。</param>
        /// <param name="width">描画先の幅（ピクセル）。</param>
        /// <param name="height">描画先の高さ（ピクセル）。</param>
        public void Draw(in Render3DContext render, in CameraPose pose, float width, float height)
        {
            if (width <= 0f || height <= 0f)
                return;

            var size = Math.Clamp(MathF.Min(width, height) * SizeRatio, MinSize, MaxSize);
            if (size + Margin * 2f > width || size + Margin * 2f > height)
                return;

            var forward = Vector3.Transform(new Vector3(0f, 0f, -1f), pose.Rotation);
            var up = Vector3.Transform(Vector3.UnitY, pose.Rotation);

            // 原点を同じ向きから見る。平行投影なので、遠近で腕の長さが変わらない。
            var view = Matrix4x4.CreateLookAt(-forward * ViewDistance, Vector3.Zero, up);
            var projection = Matrix4x4.CreateOrthographic(ViewSize, ViewSize, 0.1f, ViewDistance * 2f);

            var shared = resources.Get(render.Device);
            var transform = view * projection;

            // シーンとは無関係の枠なので、深度も見ないし書き込まない。
            var settings = new DrawSettings { IgnoreDepth = true, SkipDepthWrite = true };

            render.Context.RSSetViewport(
                new Viewport(width - size - Margin, height - size - Margin, size, size));

            foreach (var mesh in shared.Axes)
                shared.Pipeline.Draw(render.Context, TransformConstants.Create(transform, 1f), settings, mesh);

            render.Context.RSSetViewport(new Viewport(0, 0, width, height));
        }

        /// <summary>軸1本分の線。正の側は長い腕と先端、負の側は短い腕だけ。</summary>
        private static Vector3[] BuildAxis(Vector3 axis)
        {
            List<Vector3> points =
            [
                -axis * NegativeArm, axis * PositiveArm,
            ];

            points.AddRange(BuildCap(axis * PositiveArm));

            return [.. points];
        }

        /// <summary>
        /// 先端に付ける小さな八面体の枠。
        /// </summary>
        /// <remarks>
        /// どの向きから見ても同じ大きさの菱形に見えるので、視点を回しても
        /// 先端の存在が分かります。
        /// </remarks>
        private static IEnumerable<Vector3> BuildCap(Vector3 center)
        {
            Vector3[] tips =
            [
                new(CapRadius, 0f, 0f), new(-CapRadius, 0f, 0f),
                new(0f, CapRadius, 0f), new(0f, -CapRadius, 0f),
                new(0f, 0f, CapRadius), new(0f, 0f, -CapRadius),
            ];

            for (var i = 0; i < tips.Length; i++)
            {
                for (var j = i + 1; j < tips.Length; j++)
                {
                    // 向かい合う頂点どうしは辺にならない（八面体の対角線になる）。
                    if (tips[i] == -tips[j])
                        continue;

                    yield return center + tips[i];
                    yield return center + tips[j];
                }
            }
        }

        public void Dispose() => resources.Dispose();

        private sealed class IndicatorResources : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; }

            public LineMesh[] Axes { get; }

            public IndicatorResources(ID3D11Device device)
            {
                Pipeline = new RenderPipeline<TransformConstants>(
                    device, Vertex.InputElements, new VertexColorMaterial(device));

                Axes =
                [
                    new LineMesh(device, BuildAxis(Vector3.UnitX), AxisXColor),
                    new LineMesh(device, BuildAxis(Vector3.UnitY), AxisYColor),
                    new LineMesh(device, BuildAxis(Vector3.UnitZ), AxisZColor),
                ];
            }

            public void Dispose()
            {
                foreach (var mesh in Axes)
                    mesh.Dispose();

                Pipeline.Dispose();
            }
        }
    }
}
