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
    /// 派生クラスが実装するのは <see cref="Draw"/> と <see cref="GetRenderSize"/> の
    /// 2つだけです。出力用のレンダーターゲット確保・カメラ行列の解決・
    /// コマンドリストの生成は基底クラスが行います。
    /// </para>
    /// </remarks>
    public abstract class Shape3DSourceBase : IShapeSource2, I3DProvider
    {
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
        /// 出力画像の一辺の長さ（ピクセル）を返します。0 以下を返すと何も描画しません。
        /// </summary>
        protected abstract int GetRenderSize(in FrameContext itemTime);

        /// <inheritdoc/>
        public void Update(TimelineItemSourceDescription description)
        {
            var itemTime = FrameContext.FromItem(description);
            var size = GetRenderSize(itemTime);

            if (size <= 0)
            {
                output = null;
                return;
            }

            // カメラはシーン全体に属するため、アイテム内ではなくタイムライン上の
            // 位置で評価する。
            var camera = SceneCameraRegistry.Get(description);
            var view = camera.GetViewMatrix(FrameContext.FromTimeline(description));
            var projection = SceneCamera.GetProjectionMatrix(1f);

            // 出力画像はアイテムの中心を原点として扱われるため、左上へ半分ずらす。
            var offset = new Vector2(-size / 2f, -size / 2f);

            // 出力経路ではアイテムの位置や回転は YMM4 が後から適用するので、
            // ワールド行列は単位行列でよい。
            var item = new DrawContext3D
            {
                World = Matrix4x4.Identity,
                Opacity = 1f,
                Time = itemTime,
            };

            output = renderer.Render(Devices, size, size, view, projection, offset,
                render => Draw(render, item));
        }

        public virtual void Dispose()
        {
            renderer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
