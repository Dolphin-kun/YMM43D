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
    /// 図形アイテムも映像エフェクトも、出力経路では同じ手順を踏みます。描くものが
    /// ワールド空間で占める大きさから描画先の一辺を決め、画面全体との比で画角を狭め、
    /// シーンカメラのビュー行列で描く、という流れです。この手順をここにまとめています。
    /// </para>
    /// <para>
    /// アイテムの位置・拡大率・回転は YMM4 が後から適用するため、ここで組み立てる
    /// ワールド行列にはアイテム自身の変換だけを入れます。
    /// </para>
    /// </remarks>
    public sealed class Output3DRenderer : IDisposable
    {
        /// <summary>描画先の一辺の上限（ピクセル）。</summary>
        private const int MaxRenderSize = 4096;

        private readonly Renderer3DTo2D renderer = new();

        /// <summary>
        /// 3D 描画を行い、その結果を YMM4 に渡せる画像として返します。
        /// </summary>
        /// <param name="devices">YMM4 のグラフィックスデバイス。</param>
        /// <param name="description">YMM4 から渡された描画要求。</param>
        /// <param name="worldExtent">
        /// 描くものがワールド空間で占める差し渡しの大きさ。どの向きに回転しても
        /// 収まるよう、外接球の直径にあたる値を渡してください。0 以下なら何も描きません。
        /// </param>
        /// <param name="world">描くものに掛けるワールド行列。</param>
        /// <param name="draw">実際の 3D 描画。</param>
        public ID2D1Image Render(
            IGraphicsDevicesAndContext devices,
            TimelineItemSourceDescription description,
            float worldExtent,
            Matrix4x4 world,
            Draw3DCallback draw)
        {
            var itemTime = FrameContext.FromItem(description);
            var timelineTime = FrameContext.FromTimeline(description);

            // カメラはシーン全体に属するため、アイテム内ではなくタイムライン上の
            // 位置で評価する。
            var camera = SceneCameraRegistry.Get(description);
            var view = camera.GetViewMatrix(timelineTime);

            var renderSize = GetRenderSize(camera, timelineTime, worldExtent, description);
            if (renderSize <= 0)
            {
                // 描くものが無いときに Output を未設定のままにすると、YMM4 が結果を
                // 受け取る際に例外になる。空の画像を返す。
                return renderer.RenderEmpty(devices);
            }

            // 描画先は画面全体より小さいので、そのぶん画角を狭めて縮尺を合わせる。
            // こうしないと、描画先を大きくしただけで対象まで大きく見えてしまう。
            var fieldOfView = SceneCamera.GetFieldOfViewFor(renderSize, description.ScreenSize.Height);
            var projection = SceneCamera.GetProjectionMatrix(1f, fieldOfView);

            // 出力画像はアイテムの中心を原点として扱われるため、左上へ半分ずらす。
            var offset = new Vector2(-renderSize / 2f, -renderSize / 2f);

            var item = new DrawContext3D
            {
                World = world,
                Opacity = 1f,
                Time = itemTime,
            };

            return renderer.Render(devices, renderSize, renderSize, view, projection, offset,
                render => draw(render, item));
        }

        /// <summary>
        /// 対象が収まる描画先の一辺（ピクセル）を求めます。
        /// </summary>
        /// <remarks>
        /// ワールド空間での大きさを、カメラからの距離に応じて画面上のピクセル数に
        /// 換算します。カメラを近づければ大きく、遠ざければ小さくなります。
        /// </remarks>
        private static int GetRenderSize(
            SceneCamera camera,
            in FrameContext timelineTime,
            float worldExtent,
            TimelineItemSourceDescription description)
        {
            if (worldExtent <= 0)
                return 0;

            var distance = camera.Distance.GetFloat(timelineTime);
            var pixelsPerUnit = SceneCamera.GetPixelsPerUnit(distance, description.ScreenSize.Height);

            return (int)Math.Clamp(MathF.Ceiling(worldExtent * pixelsPerUnit), 0, MaxRenderSize);
        }

        public void Dispose() => renderer.Dispose();
    }
}
