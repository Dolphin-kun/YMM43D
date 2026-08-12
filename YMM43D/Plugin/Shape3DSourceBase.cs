using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D図形アイテムの描画元となる基底クラス。
    /// </summary>
    /// <remarks>
    /// このクラス1つが、3Dプレビュー（<see cref="I3DProvider"/> として直接描画される）と
    /// 動画出力（3D描画結果を 2D 画像に変換して <see cref="Output"/> に出す）の両方の
    /// 経路を受け持ちます。派生クラスが実装するのは <see cref="Draw"/> と
    /// <see cref="GetWorldBounds"/> の2つだけです。
    /// </remarks>
    public abstract class Shape3DSourceBase : IShapeSource2, I3DProvider
    {
        private readonly Output3DRenderer renderer = new();
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
        /// この図形がワールド空間で占める範囲を返します。
        /// </summary>
        /// <remarks>
        /// 出力画像の大きさを決めるのに使います。どの向きに回転しても収まる範囲を
        /// 返してください。大きさが無い範囲を返すと何も描画しません。
        /// </remarks>
        protected abstract WorldBounds GetWorldBounds(in FrameContext itemTime);

        /// <inheritdoc/>
        public void Update(TimelineItemSourceDescription description)
        {
            var itemTime = FrameContext.FromItem(description);

            // 図形アイテムには DrawDescription を返す口が無いので、アイテムの配置は
            // YMM4 が出来上がった画像に掛ける。3D 側でも同じ配置を取り込むため、
            // その逆変換を射影に畳み込んで打ち消してもらう。
            output = renderer.Render(
                Devices,
                description,
                GetWorldBounds(itemTime),
                Matrix4x4.Identity,
                Draw,
                self: this,
                hostAppliesPlacement: true);
        }

        public virtual void Dispose()
        {
            renderer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
