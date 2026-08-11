using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Integration;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D 空間への描画を委ねるためのコールバック。
    /// </summary>
    public delegate void Draw3DCallback(in Render3DContext render, DrawContext3D item);

    /// <summary>
    /// 3D 描画の結果を、YMM4 の出力に流せる 2D 画像に変換します。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 図形アイテムも映像エフェクトも、出力経路では同じ手順を踏みます。描くものの
    /// 範囲を画面に投影して必要な描画先を決め、そこにシーンカメラの視点で描く、
    /// という流れです。この手順をここにまとめています。
    /// </para>
    /// <para>
    /// アイテムの位置・拡大率・回転は YMM4 が後から適用するため、ここで組み立てる
    /// ワールド行列には対象自身の変換だけを入れます。
    /// </para>
    /// </remarks>
    public sealed class Output3DRenderer : IDisposable
    {
        /// <summary>描画先の一辺の上限（ピクセル）。</summary>
        private const int MaxRenderSize = 4096;

        /// <summary>
        /// カメラより手前に来た点を押し戻す最小の視距離。
        /// </summary>
        /// <remarks>
        /// カメラの背後や真横にある隅をそのまま投影すると発散するため、
        /// ここで頭打ちにします。
        /// </remarks>
        private const float MinViewDistance = 0.01f;

        private readonly Renderer3DTo2D renderer = new();

        /// <summary>
        /// 3D 描画を行い、その結果を YMM4 に渡せる画像として返します。
        /// </summary>
        /// <param name="devices">YMM4 のグラフィックスデバイス。</param>
        /// <param name="description">YMM4 から渡された描画要求。</param>
        /// <param name="bounds">描くものがワールド空間で占める範囲（<paramref name="world"/> を掛ける前）。</param>
        /// <param name="world">描くものに掛けるワールド行列。</param>
        /// <param name="draw">実際の 3D 描画。</param>
        public ID2D1Image Render(
            IGraphicsDevicesAndContext devices,
            TimelineItemSourceDescription description,
            WorldBounds bounds,
            Matrix4x4 world,
            Draw3DCallback draw)
        {
            // アイテムをまたいだ前後関係を出せるかどうかの調査。設計が決まったら外す。
            SceneAccessProbe.ReportOnce(description);

            var itemTime = FrameContext.FromItem(description);
            var timelineTime = FrameContext.FromTimeline(description);

            // カメラはシーン全体に属するため、アイテム内ではなくタイムライン上の
            // 位置で評価する。
            var camera = SceneCameraRegistry.Get(description);
            var view = camera.GetViewMatrix(timelineTime);

            var area = GetRenderArea(bounds.Transform(world), view, description.ScreenSize.Height);
            if (area is not { } target)
            {
                // 描くものが無いときに Output を未設定のままにすると、YMM4 が結果を
                // 受け取る際に例外になる。空の画像を返す。
                return renderer.RenderEmpty(devices);
            }

            var item = new DrawContext3D
            {
                World = world,
                Opacity = 1f,
                Time = itemTime,
            };

            return renderer.Render(
                devices, target.Width, target.Height, view, target.Projection, target.Offset,
                render => draw(render, item));
        }

        /// <summary>
        /// 描くものが画面上で占める範囲を求め、それをちょうど収める描画先を決めます。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 範囲の8隅をカメラから見た向き（<c>x / -z</c>）に直し、その最小・最大を取ります。
        /// こうすると遠近が織り込まれるため、カメラに近い面が大きく映る場合でも
        /// 端が切れません。距離だけで見積もると、厚みのあるものが手前へ張り出したときに
        /// 溢れてしまいます。
        /// </para>
        /// <para>
        /// 求めた範囲は対象の中心からずれていることがあるので、中心を合わせた正方形では
        /// なく、範囲そのものを描画先にします。射影行列も中心をずらしたものを使います。
        /// </para>
        /// </remarks>
        private static RenderArea? GetRenderArea(in WorldBounds bounds, in Matrix4x4 view, float screenHeight)
        {
            if (bounds.IsEmpty || screenHeight <= 0)
                return null;

            var minTan = new Vector2(float.MaxValue);
            var maxTan = new Vector2(float.MinValue);

            foreach (var corner in bounds.GetCorners())
            {
                var viewSpace = Vector3.Transform(corner, view);

                // 右手系のビュー空間では、カメラの前方は -Z。
                var distance = MathF.Max(-viewSpace.Z, MinViewDistance);
                var tan = new Vector2(viewSpace.X / distance, viewSpace.Y / distance);

                minTan = Vector2.Min(minTan, tan);
                maxTan = Vector2.Max(maxTan, tan);
            }

            var pixelsPerTangent = SceneCamera.GetPixelsPerTangent(screenHeight);
            var width = (int)MathF.Ceiling((maxTan.X - minTan.X) * pixelsPerTangent);
            var height = (int)MathF.Ceiling((maxTan.Y - minTan.Y) * pixelsPerTangent);

            if (width <= 0 || height <= 0)
                return null;

            // 上限に当たった場合は、その分だけ範囲を狭める（＝端が切れる）。
            // 縮尺を変えると出力上の大きさが変わってしまうため、解像度ではなく
            // 範囲の方を諦める。
            if (width > MaxRenderSize)
            {
                var excess = (width - MaxRenderSize) / (2f * pixelsPerTangent);
                minTan.X += excess;
                maxTan.X -= excess;
                width = MaxRenderSize;
            }

            if (height > MaxRenderSize)
            {
                var excess = (height - MaxRenderSize) / (2f * pixelsPerTangent);
                minTan.Y += excess;
                maxTan.Y -= excess;
                height = MaxRenderSize;
            }

            var projection = SceneCamera.GetProjectionMatrix(minTan, maxTan);

            // 出力画像はアイテムの原点を中心として扱われる。原点は視線上の
            // (0, 0) に投影されるので、そこから範囲の左上までのずれを渡す。
            // 3D の Y は上向き、2D の Y は下向きなので符号が反転する。
            var offset = new Vector2(
                minTan.X * pixelsPerTangent,
                -maxTan.Y * pixelsPerTangent);

            return new RenderArea(width, height, projection, offset);
        }

        public void Dispose() => renderer.Dispose();

        /// <summary>描画先の大きさと、そこに描くための射影。</summary>
        private readonly record struct RenderArea(
            int Width,
            int Height,
            Matrix4x4 Projection,
            Vector2 Offset);
    }
}
