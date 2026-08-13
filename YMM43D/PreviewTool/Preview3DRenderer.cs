using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Camera;
using YMM43D.Plugin;
using YMM43D.PreviewTool.Rendering;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class Preview3DRenderer : IDisposable
    {
        private static readonly Color4 BackgroundColor = new(0.15f, 0.15f, 0.15f, 1f);

        private readonly GridRenderer grid = new();
        private readonly CameraGizmoRenderer cameraGizmo = new();
        private readonly FlatItemProvider flatItemProvider = new();
        private readonly ItemDrawContextBuilder contextBuilder = new();

        // 掴む判定は、最後に描いたときの見え方を使う。描く側と別に組み立て直すと、
        // 画面に出ているものと掴めるものがずれる。
        private PickTarget[] pickTargets = [];
        private Matrix4x4 lastViewProjection = Matrix4x4.Identity;
        private float lastWidth;
        private float lastHeight;

        public I3DProvider DefaultProvider => flatItemProvider;

        /// <summary>掴める見た目を1つ分。</summary>
        internal readonly record struct PickTarget(IVideoItem Item, Matrix4x4 World);

        public void Draw(
            ID3D11Device device,
            ID3D11DeviceContext context,
            ID3D11RenderTargetView renderTarget,
            ID3D11DepthStencilView depthStencil,
            int width,
            int height,
            PreviewScene scene)
        {
            // 描画情報の組み立ては、アイテムの 2D 描画やエフェクトの評価を伴い、
            // その過程で同じコンテキストに別の 3D 描画が走ることがある。
            // パスを開いた後に呼ぶと描画先やビューポートが入れ替わってしまうため、
            // 必要な情報をすべて先に揃えてから描き始める。
            var drawContexts = new DrawContext3D[scene.Items.Count];
            for (var i = 0; i < scene.Items.Count; i++)
            {
                var previewItem = scene.Items[i];
                drawContexts[i] = contextBuilder.Build(
                    previewItem.Item,
                    previewItem.GetItemTime(scene.Time),
                    scene.Environment,
                    previewItem.Provider);
            }

            contextBuilder.RetainOnly(scene.Items.Select(i => i.Item).ToHashSet());

            context.OMSetRenderTargets(renderTarget, depthStencil);
            context.ClearRenderTargetView(renderTarget, BackgroundColor);
            context.ClearDepthStencilView(depthStencil, DepthStencilClearFlags.Depth, 1f, 0);
            context.RSSetViewport(new Viewport(0, 0, width, height));

            var viewPose = scene.ViewPose;

            // 画角はシーンカメラの設定と動画の画面の高さで決まる。プレビュー用の
            // 視点をどこへ動かしても、写り方の縮尺は出力と揃う。
            var pixelsPerTangent = SceneProjection.GetPixelsPerTangent(scene.SceneCamera, scene.ScreenHeight);

            var projection = SceneProjection.GetProjectionMatrix(
                (float)width / Math.Max(1, height), scene.ScreenHeight, pixelsPerTangent);

            var render = new Render3DContext(device, context, viewPose.ViewMatrix, projection);

            grid.Draw(render, viewPose.Position);
            cameraGizmo.Draw(render, scene.SceneCameraPose, scene.GetScreenTangent(pixelsPerTangent));

            for (var i = 0; i < scene.Items.Count; i++)
                scene.Items[i].Provider.Draw(render, drawContexts[i]);

            pickTargets =
            [
                .. scene.Items.Select((item, i) => new PickTarget(item.Item, drawContexts[i].World))
            ];
            lastViewProjection = viewPose.ViewMatrix * projection;
            lastWidth = width;
            lastHeight = height;
        }

        /// <summary>画面上の位置から、ワールド空間へ伸ばした視線を作ります。</summary>
        /// <param name="position">描画先の左上を原点としたピクセル位置。</param>
        public PickRay? CreateRay(Vector2 position)
            => PickRay.FromScreen(position, lastWidth, lastHeight, lastViewProjection);

        /// <summary>
        /// 画面上の位置にあるアイテムのうち、いちばん手前のものを返します。
        /// </summary>
        /// <param name="position">描画先の左上を原点としたピクセル位置。</param>
        /// <param name="ray">その位置から伸ばした視線。掴んだ後の移動に使います。</param>
        public PickTarget? Pick(Vector2 position, out PickRay ray)
        {
            ray = default;

            if (CreateRay(position) is not { } cast)
                return null;

            ray = cast;

            PickTarget? found = null;
            var nearest = float.PositiveInfinity;

            foreach (var target in pickTargets)
            {
                if (cast.IntersectUnitBox(target.World) is not { } distance || distance >= nearest)
                    continue;

                nearest = distance;
                found = target;
            }

            return found;
        }

        public void Dispose()
        {
            grid.Dispose();
            cameraGizmo.Dispose();
            flatItemProvider.Dispose();
            contextBuilder.Dispose();
        }
    }

    internal sealed class PreviewScene
    {
        public required CameraPose ViewPose { get; init; }

        public required CameraState SceneCamera { get; init; }

        public CameraPose SceneCameraPose => SceneCamera.GetPose();

        public float ScreenWidth
            => Environment.SourceDescription?.ScreenSize.Width ?? 0f;

        public float ScreenHeight
            => Environment.SourceDescription?.ScreenSize.Height ?? 0f;

        /// <summary>
        /// 画面の右端・下端が、視線からどれだけ傾いているか（正接）。
        /// </summary>
        /// <remarks>
        /// カメラの枠を動画と同じ縦横比・同じ画角で描くのに使います。画面の大きさが
        /// 分からない場面では、16:9 に見立てておきます。
        /// </remarks>
        public Vector2 GetScreenTangent(float pixelsPerTangent)
        {
            if (ScreenWidth <= 0f || ScreenHeight <= 0f || pixelsPerTangent <= 0f)
                return new Vector2(0.96f, 0.54f);

            return new Vector2(ScreenWidth, ScreenHeight) / (2f * pixelsPerTangent);
        }

        public required FrameContext Time { get; init; }

        public required PreviewEnvironment Environment { get; init; }

        public required IReadOnlyList<PreviewItem> Items { get; init; }
    }
}
