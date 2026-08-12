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
    /// 図形アイテムも映像エフェクトも、出力経路では同じ手順を踏みます。描くものの
    /// 範囲を画面に投影して必要な描画先を決め、そこにシーンカメラの視点で描く、
    /// という流れです。
    /// </remarks>
    public sealed class Output3DRenderer : IDisposable
    {
        /// <summary>描画先の一辺の上限（ピクセル）。</summary>
        private const int MaxRenderSize = 4096;

        /// <summary>カメラより手前に来た点を押し戻す最小の視距離。</summary>
        /// <remarks>背後や真横にある隅をそのまま投影すると発散します。</remarks>
        private const float MinViewDistance = 0.01f;

        /// <summary>描画先として認める、視線からの傾きの上限。</summary>
        /// <remarks>
        /// 画面に写るのはせいぜい傾き1程度まで。大きく超えた範囲は見えないうえ、
        /// 描画先のずれが桁外れの値になって Direct2D を壊します。
        /// </remarks>
        private const float MaxTangent = 64f;

        private readonly Renderer3DTo2D renderer = new();

        /// <summary>
        /// 3D 描画を行い、その結果を YMM4 に渡せる画像として返します。
        /// </summary>
        /// <param name="bounds">描くものがワールド空間で占める範囲（<paramref name="world"/> を掛ける前）。</param>
        /// <param name="world">描くものに掛けるワールド行列。</param>
        /// <param name="self">
        /// いま描こうとしているプロバイダー。同じシーンにある他の 3D 物体との前後関係を
        /// 出すために使います。<c>null</c> を渡すと、自分だけを描きます。
        /// </param>
        /// <param name="hostAppliesPlacement">
        /// YMM4 が出来上がった画像にアイテムの配置（位置・拡大率・回転）を掛けるなら
        /// <c>true</c>。図形アイテムがこれにあたります。映像エフェクトは
        /// <c>DrawDescription</c> を無効化して自分で配置するため <c>false</c> です。
        /// </param>
        /// <param name="placement">
        /// このアイテムの配置行列。省略するとタイムライン上のアイテム設定から組み立てます。
        /// <c>DrawDescription</c> を読める呼び出し側は、そちらから作ったものを渡してください。
        /// 描画元の都合や前段のエフェクトまで織り込まれているぶん正確です。
        /// </param>
        public ID2D1Image Render(
            IGraphicsDevicesAndContext devices,
            TimelineItemSourceDescription description,
            WorldBounds bounds,
            Matrix4x4 world,
            Draw3DCallback draw,
            I3DProvider? self = null,
            bool hostAppliesPlacement = false,
            Matrix4x4? placement = null)
        {
            var itemTime = FrameContext.FromItem(description);
            var timelineTime = FrameContext.FromTimeline(description);

            // カメラはシーン全体に属するため、アイテム内ではなくタイムライン上の
            // 位置で評価する。
            var camera = SceneCameraRegistry.Get(description);
            var view = camera.GetViewMatrix(timelineTime);
            var pixelsPerTangent = SceneCamera.GetPixelsPerTangent(camera.Distance.GetFloat(timelineTime));

            // シーン内での自分の居場所と、他の 3D 物体を調べる。
            var scene = SceneDepthCollector.Collect(description, self);

            // アイテムの位置・拡大率・回転はワールド行列に取り込む。こうしないと、
            // 深度がアイテムごとに別の空間で測られ、前後関係が食い違う。
            var placedWorld = world * (placement ?? scene.OwnerPlacement);

            // 取り込んだ配置を YMM4 も画像に掛けるなら、その逆変換を射影に畳み込んで
            // 打ち消す。畳み込むので、Direct2D 側には変換が一切残らない。
            var tangentToImage = ImageProjection.TangentToImage(
                pixelsPerTangent,
                hostAppliesPlacement ? scene.OwnerScreenPlacement : ScreenPlacement.None);

            var area = GetRenderArea(bounds, placedWorld, view, tangentToImage);
            if (area is not { } target)
            {
                // 描くものが無いときに Output を未設定のままにすると、YMM4 が結果を
                // 受け取る際に例外になる。空の画像を返す。
                return renderer.RenderEmpty(devices);
            }

            var item = new DrawContext3D
            {
                World = placedWorld,
                Opacity = 1f,
                Time = itemTime,
            };

            var projection = ImageProjection.Compose(
                tangentToImage, target.Origin, target.Width, target.Height);

            return renderer.Render(
                devices, target.Width, target.Height, view, projection, target.Origin,
                render =>
                {
                    // 自分より手前にあるものに隠されるよう、先に深度だけ埋めておく。
                    DrawOccluders(render, scene.Occluders);
                    draw(render, item);
                });
        }

        /// <summary>
        /// 他のアイテムの形を、色を書かずに深度バッファへ埋めます。
        /// </summary>
        /// <remarks>
        /// 描画そのものは各プロバイダーに任せます。<see cref="DrawContext3D.DepthOnly"/> が
        /// <see cref="DrawSettings"/> まで伝わるため、プロバイダー側の実装は変えずに済みます。
        /// </remarks>
        private static void DrawOccluders(
            in Render3DContext render,
            IReadOnlyList<SceneDepthCollector.Occluder> occluders)
        {
            foreach (var occluder in occluders)
            {
                var context = new DrawContext3D
                {
                    World = occluder.World,
                    Opacity = 1f,
                    Time = occluder.Time,
                    DepthOnly = true,
                };

                try
                {
                    occluder.Provider.Draw(render, context);
                }
                catch
                {
                    // 他のアイテムの都合で自分の描画まで巻き込まれないようにする。
                    // 隠れ方が甘くなるだけで、絵は出る。
                }
            }
        }

        /// <summary>
        /// 描くものが画面上で占める範囲を求め、それをちょうど収める描画先を決めます。
        /// </summary>
        /// <remarks>
        /// 範囲の8隅をカメラから見た向き（<c>x / -z</c>）に直し、アイテムの画像空間まで
        /// 移してから最小・最大を取ります。遠近が織り込まれるので、厚みのあるものが
        /// 手前へ張り出しても端が切れません。
        /// <para>
        /// ワールド行列は隅ごとに掛けます。<see cref="WorldBounds"/> は軸に平行な箱なので、
        /// 先に掛けて箱に戻すと回転のたびに膨らみ、30 度で 1.87 倍にもなります。
        /// </para>
        /// </remarks>
        private static RenderArea? GetRenderArea(
            in WorldBounds bounds,
            in Matrix4x4 world,
            in Matrix4x4 view,
            in Matrix3x2 tangentToImage)
        {
            if (bounds.IsEmpty)
                return null;

            var worldView = world * view;

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            foreach (var corner in bounds.GetCorners())
            {
                var viewSpace = Vector3.Transform(corner, worldView);

                // 右手系のビュー空間では、カメラの前方は -Z。
                var depth = MathF.Max(-viewSpace.Z, MinViewDistance);
                var tangent = new Vector2(viewSpace.X / depth, viewSpace.Y / depth);

                // 真横や背後の隅は傾きが際限なく大きくなる。そのまま渡すと Direct2D が落ちる。
                if (!IsUsable(tangent))
                    return null;

                tangent = Vector2.Clamp(tangent, new Vector2(-MaxTangent), new Vector2(MaxTangent));

                var image = Vector2.Transform(tangent, tangentToImage);
                if (!IsUsable(image))
                    return null;

                min = Vector2.Min(min, image);
                max = Vector2.Max(max, image);
            }

            var width = (int)MathF.Ceiling(max.X - min.X);
            var height = (int)MathF.Ceiling(max.Y - min.Y);

            if (width <= 0 || height <= 0)
                return null;

            // 上限に当たったら範囲の方を諦める（＝端が切れる）。縮尺を変えると
            // 出力上の大きさが変わってしまうため。
            if (width > MaxRenderSize)
            {
                min.X += (width - MaxRenderSize) / 2f;
                width = MaxRenderSize;
            }

            if (height > MaxRenderSize)
            {
                min.Y += (height - MaxRenderSize) / 2f;
                height = MaxRenderSize;
            }

            return new RenderArea(width, height, min);
        }

        /// <summary>座標として計算に使える値かどうかを調べます。</summary>
        private static bool IsUsable(in Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        public void Dispose() => renderer.Dispose();

        /// <summary>描画先の大きさと、アイテムの画像の中でのその左上。</summary>
        private readonly record struct RenderArea(int Width, int Height, Vector2 Origin);
    }
}
