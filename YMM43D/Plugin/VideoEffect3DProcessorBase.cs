using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YMM43D.Integration;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D 描画を行う映像エフェクトの、描画処理側の基底クラス。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 入力画像を受け取り、それをもとに 3D を描いて、その結果を <see cref="Output"/> に
    /// 出します。3Dプレビューからは <see cref="Draw"/> が直接呼ばれるので、
    /// 動画出力とプレビューで同じ見た目になります。
    /// </para>
    /// <para>
    /// 入力画像は 3D 描画用デバイスのテクスチャに変換して <see cref="GetTexture"/> から
    /// 取り出せます。<see cref="Draw"/> では <see cref="DrawContext3D.Texture"/> を優先し、
    /// それが無ければこのメソッドを呼んでください。
    /// </para>
    /// <para>
    /// 派生クラスが実装するのは <see cref="Draw"/> と <see cref="GetWorldExtent"/> の
    /// 2つだけです。描画先の大きさの決定・カメラ行列の解決・入力画像のテクスチャ化は
    /// この基底クラスが行います。
    /// </para>
    /// </remarks>
    public abstract class VideoEffect3DProcessorBase : IVideoEffectProcessor, I3DVideoEffect, I3DSizeProvider
    {
        private readonly VideoEffect3DBase? owner;
        private readonly D2DTextureBridge textureBridge = new();
        private readonly Output3DRenderer renderer = new();

        private ID2D1Image? output;
        private Vector2 inputSize;
        private Vector2 inputOffset;

        /// <summary>YMM4 のグラフィックスデバイス。</summary>
        protected IGraphicsDevicesAndContext Devices { get; }

        /// <summary>直前に受け取った入力画像。</summary>
        protected ID2D1Image? Input { get; private set; }

        /// <summary>直近の <see cref="Update"/> で受け取った描画要求。</summary>
        public EffectDescription? EffectDescription { get; private set; }

        /// <param name="owner">
        /// このプロセッサを生み出したエフェクト。破棄時に結び付きを解くために使います。
        /// </param>
        /// <param name="devices">YMM4 のグラフィックスデバイス。</param>
        protected VideoEffect3DProcessorBase(VideoEffect3DBase? owner, IGraphicsDevicesAndContext devices)
        {
            this.owner = owner;
            Devices = devices;
        }

        /// <inheritdoc/>
        public ID2D1Image Output => output ?? throw new InvalidOperationException(
            "まだ画像が生成されていません。Update を先に呼んでください。");

        /// <inheritdoc/>
        /// <remarks>
        /// 入力画像は自分でテクスチャ化するため、呼び出し側に用意してもらう必要はありません。
        /// </remarks>
        public virtual bool RequiresMappedTexture => false;

        /// <summary>
        /// 3D空間に描画します。プレビューと出力の両方から呼ばれます。
        /// </summary>
        public abstract void Draw(in Render3DContext render, DrawContext3D item);

        /// <summary>
        /// 描くものが占める範囲を返します。<see cref="Update"/> が組み立てる
        /// ワールド行列を掛ける前の座標系で答えてください。
        /// </summary>
        /// <remarks>
        /// 出力画像の大きさを決めるのに使います。大きさが無い範囲を返すと
        /// 何も描画しません。入力画像の実寸は <see cref="TryGetSize"/> で得られます。
        /// </remarks>
        protected abstract WorldBounds GetLocalBounds(in FrameContext itemTime);

        /// <inheritdoc/>
        public DrawDescription Update(EffectDescription description)
        {
            EffectDescription = description;

            // ワールド行列を組み立てるのに入力画像の実寸が要る。テクスチャ化は
            // 描画時まで遅らせられるが、範囲だけは先に調べておく。
            if (Input is { } input)
            {
                var bounds = textureBridge.GetBounds(Devices, input);
                inputSize = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                inputOffset = new Vector2(bounds.Left, bounds.Top);
            }

            var itemTime = FrameContext.FromItem(description);

            // プレビュー経路では呼び出し側が実寸をワールド行列に入れてくれるが、
            // 出力経路では自分で入れる必要がある。両者で見た目を揃えるため、
            // ここでも同じ換算を掛ける。
            var world = TryGetSize(out var size, out var offset)
                ? WorldScale.CreateSizeMatrix(size, offset + size / 2f)
                : Matrix4x4.Identity;

            output = renderer.Render(Devices, description, GetLocalBounds(itemTime), world, Draw);

            return description.DrawDescription;
        }

        /// <inheritdoc/>
        public void SetInput(ID2D1Image? input) => Input = input;

        /// <inheritdoc/>
        public void ClearInput() => Input = null;

        /// <summary>
        /// 入力画像を 3D 描画用デバイスのテクスチャとして取得します。
        /// </summary>
        /// <param name="device">テクスチャを使うデバイス。</param>
        /// <remarks>
        /// 呼ばれるたびに最新の入力内容を焼き直すため、内容が古くなることはありません。
        /// </remarks>
        public ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            if (Input is null)
                return null;

            var texture = textureBridge.GetTexture(device, Devices, Input, this, out var bounds);
            if (texture is not null)
            {
                inputSize = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                inputOffset = new Vector2(bounds.Left, bounds.Top);
            }

            return texture;
        }

        /// <inheritdoc/>
        public bool TryGetSize(out Vector2 size, out Vector2 offset)
        {
            size = inputSize;
            offset = inputOffset;
            return size.X > 0 && size.Y > 0;
        }

        public virtual void Dispose()
        {
            owner?.DetachProcessor(this);
            renderer.Dispose();
            textureBridge.Dispose();
            Input = null;
            output = null;

            GC.SuppressFinalize(this);
        }
    }
}
