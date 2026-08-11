using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Integration;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D図形アイテムの描画元となる基底クラス。
    /// </summary>
    /// <remarks>
    /// <para>
    /// このクラス1つが、3Dプレビュー（<see cref="I3DProvider"/> として直接描画される）と
    /// 動画出力（3D描画結果を 2D 画像に変換して <see cref="Output"/> に出す）の
    /// 両方の経路を受け持ちます。
    /// </para>
    /// <para>
    /// 派生クラスが実装するのは <see cref="Draw"/> と <see cref="GetWorldExtent"/> の
    /// 2つだけです。描画先の大きさの決定・カメラ行列の解決・コマンドリストの生成は
    /// 基底クラスが行います。
    /// </para>
    /// </remarks>
    public abstract class Shape3DSourceBase : IShapeSource2, I3DProvider
    {
        /// <summary>描画先の一辺の上限（ピクセル）。</summary>
        private const int MaxRenderSize = 4096;

        private readonly Renderer3DTo2D renderer = new();
        private ID2D1Image? output;

        /// <summary>YMM4 のグラフィックスデバイス。</summary>
        protected IGraphicsDevicesAndContext Devices { get; }

        protected Shape3DSourceBase(IGraphicsDevicesAndContext devices)
        {
            Devices = devices;
        }

        /// <inheritdoc/>
        public ID2D1Image Output => output ?? throw new InvalidOperationException(
            "まだ画像が生成されていません。Update を先に呼んでください。");

        /// <inheritdoc/>
        public virtual IEnumerable<VideoController> Controllers => [];

        /// <inheritdoc/>
        public virtual bool RequiresMappedTexture => false;

        /// <summary>
        /// 3D空間にこの図形を描画します。プレビューと出力の両方から呼ばれます。
        /// </summary>
        public abstract void Draw(in Render3DContext render, DrawContext3D item);

        /// <summary>
        /// この図形がワールド空間で占める差し渡しの大きさ（単位）を返します。
        /// </summary>
        /// <remarks>
        /// 出力画像の大きさを決めるのに使います。どの向きに回転しても収まるよう、
        /// 外接球の直径にあたる値を返してください。0 以下を返すと何も描画しません。
        /// </remarks>
        protected abstract float GetWorldExtent(in FrameContext itemTime);

        /// <inheritdoc/>
        public void Update(TimelineItemSourceDescription description)
        {
            var itemTime = FrameContext.FromItem(description);
            var timelineTime = FrameContext.FromTimeline(description);

            // カメラはシーン全体に属するため、アイテム内ではなくタイムライン上の
            // 位置で評価する。
            var camera = SceneCameraRegistry.Get(description);
            var view = camera.GetViewMatrix(timelineTime);

            var renderSize = (int)GetRenderSize(camera, timelineTime, itemTime, description);
            if (renderSize <= 0)
            {
                // 大きさが 0 のときは何も描かない。Output を未設定のままにすると
                // YMM4 が結果を受け取る際に例外になるため、空の画像を返す。
                output = renderer.RenderEmpty(Devices);
                return;
            }

            // 描画先は画面全体より小さいので、そのぶん画角を狭めて縮尺を合わせる。
            // こうしないと、描画先を大きくしただけで図形まで大きく見えてしまう。
            var fieldOfView = SceneCamera.GetFieldOfViewFor(renderSize, description.ScreenSize.Height);
            var projection = SceneCamera.GetProjectionMatrix(1f, fieldOfView);

            // 出力画像はアイテムの中心を原点として扱われるため、左上へ半分ずらす。
            var offset = new Vector2(-renderSize / 2f, -renderSize / 2f);

            // 出力経路ではアイテムの位置や回転は YMM4 が後から適用するので、
            // ワールド行列は単位行列でよい。
            var item = new DrawContext3D
            {
                World = Matrix4x4.Identity,
                Opacity = 1f,
                Time = itemTime,
            };

            output = renderer.Render(Devices, renderSize, renderSize, view, projection, offset,
                render => Draw(render, item));
        }

        /// <summary>
        /// 図形が収まる描画先の一辺（ピクセル）を求めます。
        /// </summary>
        /// <remarks>
        /// 図形のワールド空間での大きさを、カメラからの距離に応じて画面上の
        /// ピクセル数に換算します。カメラを近づければ大きく、遠ざければ小さくなります。
        /// </remarks>
        private float GetRenderSize(
            SceneCamera camera,
            in FrameContext timelineTime,
            in FrameContext itemTime,
            TimelineItemSourceDescription description)
        {
            var extent = GetWorldExtent(itemTime);
            if (extent <= 0)
                return 0;

            var distance = camera.Distance.GetFloat(timelineTime);
            var pixelsPerUnit = SceneCamera.GetPixelsPerUnit(distance, description.ScreenSize.Height);

            return Math.Clamp(MathF.Ceiling(extent * pixelsPerUnit), 0, MaxRenderSize);
        }

        public virtual void Dispose()
        {
            renderer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
