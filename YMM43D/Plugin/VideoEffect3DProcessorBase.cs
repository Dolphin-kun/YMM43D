using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    /// <summary>
    /// 3D 描画を行う映像エフェクトの、描画処理側の基底クラス。
    /// </summary>
    /// <remarks>
    /// 入力画像を受け取り、それをもとに 3D を描いて、その結果を <see cref="Output"/> に
    /// 出します。3Dプレビューからは <see cref="Draw"/> が直接呼ばれるので、動画出力と
    /// プレビューで同じ見た目になります。
    /// <para>
    /// 派生クラスが実装するのは <see cref="Draw"/> と <see cref="GetLocalBounds"/> の
    /// 2つだけです。描画先の大きさの決定・カメラ行列の解決・入力画像のテクスチャ化は
    /// この基底クラスが行います。
    /// </para>
    /// </remarks>
    public abstract class VideoEffect3DProcessorBase
        : IVideoEffectProcessor, I3DVideoEffect, I3DSizeProvider, I3DLocalTransform, I3DBounds
    {
        private readonly VideoEffect3DBase? owner;
        private readonly D2DTextureBridge textureBridge = new();
        private readonly Output3DRenderer renderer = new();

        private ID2D1Image? output;
        private Vector2 inputSize;
        private Vector2 inputOffset;
        private Matrix4x4? localMatrix;
        private DeviceLease? lease;
        private ID3D11ShaderResourceView? bakedTexture;
        private nint bakedDeviceKey;
        private bool isDisposed;

        /// <summary>YMM4 のグラフィックスデバイス。</summary>
        protected IGraphicsDevicesAndContext Devices { get; }

        /// <summary>直前に受け取った入力画像。</summary>
        protected ID2D1Image? Input { get; private set; }

        /// <summary>直近の <see cref="Update"/> で受け取った描画要求。</summary>
        public EffectDescription? EffectDescription { get; private set; }

        /// <param name="owner">
        /// このプロセッサを生み出したエフェクト。破棄時に結び付きを解くために使います。
        /// </param>
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
        /// 入力画像の実寸をワールド行列に取り込むかどうか。
        /// </summary>
        /// <remarks>
        /// <c>true</c>（既定）のとき、<see cref="Draw"/> に渡るワールド行列は 1×1 の板を
        /// 入力画像の大きさに広げる変換を含み、<see cref="GetLocalBounds"/> も 1×1 の板を
        /// 基準に答えます。<c>false</c> にすると実寸は取り込まれず、すべてワールド単位で
        /// 扱えます。縦横で倍率が変わらないので、粒や線のように太さを持つものを歪ませません。
        /// </remarks>
        public virtual bool ScalesToInputSize => true;

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
        WorldBounds I3DBounds.GetLocalBounds(in FrameContext itemTime) => GetLocalBounds(itemTime);

        /// <inheritdoc/>
        public DrawDescription Update(EffectDescription description)
        {
            EffectDescription = description;

            // 入力画像に触れてよいのはこの中だけ。ここは YMM4 の描画スレッドで、
            // 画像が生きていることを YMM4 が保証してくれる唯一の場所。
            BakeInput();

            var itemTime = FrameContext.FromItem(description);

            // プレビュー経路では呼び出し側が実寸をワールド行列に入れてくれるが、
            // 出力経路では自分で入れる必要がある。両者で見た目を揃えるため、
            // ここでも同じ換算を掛ける。
            var world = ScalesToInputSize && TryGetSize(out var size, out var offset)
                ? WorldScale.CreateSizeMatrix(size, offset + size / 2f)
                : Matrix4x4.Identity;

            ConsumeCamera(description.DrawDescription, ref world);

            // 他のアイテムがこの物体を遮蔽物として扱うときに参照する。
            // 実寸とカメラ変換を合わせたもので、外からは組み立て直せない。
            localMatrix = world;

            // 3Dプレビューがアイテムから辿るのはエフェクト本体なので、自分の代わりに
            // そちらを渡す。渡さないと自分自身を遮蔽物として数えてしまう。
            output = renderer.Render(
                Devices, description, GetLocalBounds(itemTime), world, Draw,
                self: (I3DProvider?)owner ?? this,
                placement: ToWorldPlacement(description.DrawDescription));

            return Neutralize(description.DrawDescription);
        }

        private static Matrix4x4 ToWorldPlacement(DrawDescription draw)
        {
            var zoom = draw.Zoom;

            // 奥行きには縦横のどちらを使うべきか決まらないので、平均を使う。
            var scale = new Vector3(
                (float)zoom.X,
                (float)zoom.Y,
                (float)(zoom.X + zoom.Y) / 2f);

            // YMM4 の回転は時計回り、3D空間は反時計回りなので符号を反転する。
            var rotation = Rotation3D.ForObject(
                -(float)draw.Rotation.X, -(float)draw.Rotation.Y, -(float)draw.Rotation.Z);

            // YMM4 の Y 軸は下向き、3D空間は上向き。
            var translation = Matrix4x4.CreateTranslation(
                WorldScale.ToWorld((float)draw.Draw.X),
                -WorldScale.ToWorld((float)draw.Draw.Y),
                WorldScale.ToWorld((float)draw.Draw.Z));

            return Matrix4x4.CreateScale(scale) * rotation * translation;
        }

        private static DrawDescription Neutralize(DrawDescription draw) => draw with
        {
            Draw = Vector3.Zero,
            CenterPoint = Vector2.Zero,
            Zoom = Vector2.One,
            Rotation = Vector3.Zero,
            Camera = Matrix4x4.Identity,
        };

        private static void ConsumeCamera(DrawDescription draw, ref Matrix4x4 world)
        {
            if (draw.Camera == Matrix4x4.Identity)
                return;

            world *= WorldScale.ToYUpMatrix(draw.Camera);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 入力の差し替えは本体の描画スレッドから、参照は 3Dプレビューのスレッドからも
        /// 起きます。差し替えの途中に破棄済みの画像を掴まないよう、Direct2D の操作と
        /// 同じ鍵で守ります。
        /// </remarks>
        public void SetInput(ID2D1Image? input)
        {
            lock (D2DGate.Sync)
                Input = input;
        }

        /// <inheritdoc/>
        public void ClearInput()
        {
            lock (D2DGate.Sync)
                Input = null;
        }

        private void BakeInput()
        {
            if (Input is not { } input)
                return;

            // 焼き込みには 3D 側のデバイスが要る。毎回借り直すと、プレビューを
            // 閉じている間はデバイスの作り直しになるため、借りたまま持っておく。
            var device = (lease ??= GraphicsDevicePool.Acquire()).Device;

            var texture = textureBridge.GetTexture(device, Devices, input, this, out var bounds);

            inputSize = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            inputOffset = new Vector2(bounds.Left, bounds.Top);

            bakedTexture = texture;
            bakedDeviceKey = texture is null ? nint.Zero : device.NativePointer;
        }

        /// <summary>
        /// 焼き込み済みの入力画像を、テクスチャとして取得します。
        /// </summary>
        /// <remarks>
        /// 3Dプレビューのスレッドからも呼ばれます。焼いた結果を返すだけで、YMM4 が
        /// 所有する画像には触れません（理由は <see cref="BakeInput"/> を参照）。
        /// まだ <see cref="Update"/> が呼ばれていなければ <c>null</c> を返すので、
        /// 呼び出し側はアイテムの画像を自分で用意する経路に切り替えてください。
        /// </remarks>
        public ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            lock (D2DGate.Sync)
            {
                if (isDisposed || bakedDeviceKey == nint.Zero)
                    return null;

                return bakedDeviceKey == device.NativePointer ? bakedTexture : null;
            }
        }

        /// <inheritdoc/>
        public bool TryGetLocalMatrix(out Matrix4x4 matrix)
        {
            matrix = localMatrix ?? Matrix4x4.Identity;
            return localMatrix.HasValue;
        }

        /// <inheritdoc/>
        public bool TryGetSize(out Vector2 size, out Vector2 offset)
        {
            size = inputSize;
            offset = inputOffset;
            return size.X > 0 && size.Y > 0;
        }

        /// <remarks>
        /// 破棄も鍵の中で行います。3Dプレビューのスレッドが焼き込み済みテクスチャを
        /// 読んでいる最中に、YMM4 の描画スレッドがこのプロセッサを捨てることがあります。
        /// </remarks>
        public virtual void Dispose()
        {
            lock (D2DGate.Sync)
            {
                isDisposed = true;
                bakedTexture = null;
                bakedDeviceKey = nint.Zero;
                Input = null;
                output = null;
            }

            owner?.DetachProcessor(this);
            renderer.Dispose();
            textureBridge.Dispose();

            lease?.Dispose();
            lease = null;

            GC.SuppressFinalize(this);
        }
    }
}
