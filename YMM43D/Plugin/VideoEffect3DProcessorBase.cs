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
    public abstract class VideoEffect3DProcessorBase
        : IVideoEffectProcessor, I3DVideoEffect, I3DSizeProvider, I3DLocalTransform
    {
        private readonly VideoEffect3DBase? owner;
        private readonly D2DTextureBridge textureBridge = new();
        private readonly Output3DRenderer renderer = new();

        private ID2D1Image? output;
        private Vector2 inputSize;
        private Vector2 inputOffset;
        private Matrix4x4? localMatrix;

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

        /// <summary>
        /// YMM4 が画像に掛けようとしている配置を、3D のワールド行列に直します。
        /// </summary>
        /// <remarks>
        /// <para>
        /// アイテムのプロパティ（位置・拡大率・回転）ではなく、<c>DrawDescription</c> を
        /// 使うのが要点です。ここに入っているのは、アイテムの設定に加えて描画元の都合や
        /// 前段のエフェクトまで織り込んだ<b>最終的な</b>配置だからです。
        /// </para>
        /// <para>
        /// たとえば画像アイテムは、実寸と表示したい大きさの差を <c>Zoom</c> に載せてきます。
        /// アイテムのプロパティだけを見ていると、この分が抜け落ちて大きさが変わります。
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// YMM4 に配置させないための <see cref="DrawDescription"/> を作ります。
        /// </summary>
        /// <remarks>
        /// <para>
        /// アイテムの位置・拡大率・回転・カメラは、すべて 3D のワールド行列に取り込んで
        /// 描画済みです。YMM4 が同じものを画像にも掛けると二重になるため、ここで
        /// 打ち消すのではなく、はじめから何もしないよう伝えます。
        /// </para>
        /// <para>
        /// 打ち消す方式だと、位置・回転・拡大率をそれぞれ別の仕組み（描画先のずれ・
        /// Direct2D の変換・射影の縮尺）で相殺することになり、互いの干渉を確かめる術が
        /// ありません。返す値を空にしてしまえば、その計算自体が要らなくなります。
        /// </para>
        /// <para>
        /// 副作用として、このエフェクトより<b>後ろ</b>に置いたエフェクトからは、アイテムの
        /// 位置や拡大率が既定値に見えます。それらを見るエフェクトは前に置いてください。
        /// </para>
        /// </remarks>
        private static DrawDescription Neutralize(DrawDescription draw) => draw with
        {
            Draw = Vector3.Zero,
            CenterPoint = Vector2.Zero,
            Zoom = Vector2.One,
            Rotation = Vector3.Zero,
            Camera = Matrix4x4.Identity,
        };

        /// <summary>
        /// 前段のエフェクトが作った変換を 3D の形そのものに掛け、後段には渡さないようにします。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 「3D回転」や「回り込みカメラ」は <c>DrawDescription.Camera</c> に変換を書き込みます。
        /// これをそのまま後段に流すと、YMM4 が出来上がった平らな絵に 2D の変形として掛けてしまい、
        /// 立体が板のまま歪みます。形の側に掛けてしまえば、3Dプレビューと同じ見た目になります。
        /// </para>
        /// <para>
        /// 見えるのは自分より<b>前</b>に置かれたエフェクトの分だけです。連鎖の後ろに置かれた
        /// カメラ系エフェクトは、この時点ではまだ実行されていないため取り込めません。
        /// 立体化するエフェクトは、カメラ系エフェクトより後ろに置いてください。
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// 入力画像を 3D 描画用デバイスのテクスチャとして取得します。
        /// </summary>
        /// <param name="device">テクスチャを使うデバイス。</param>
        /// <remarks>
        /// 呼ばれるたびに最新の入力内容を焼き直すため、内容が古くなることはありません。
        /// </remarks>
        public ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            // 入力の参照を掴んでから使い終えるまでの間に差し替えられないようにする。
            lock (D2DGate.Sync)
            {
                if (Input is not { } input)
                    return null;

                var texture = textureBridge.GetTexture(device, Devices, input, this, out var bounds);
                if (texture is not null)
                {
                    inputSize = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                    inputOffset = new Vector2(bounds.Left, bounds.Top);
                }

                return texture;
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
