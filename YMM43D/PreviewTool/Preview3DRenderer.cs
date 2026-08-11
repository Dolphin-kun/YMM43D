using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Plugin;
using YMM43D.PreviewTool.Rendering;
using YMM43D.Scene3D;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// 3Dプレビューの 1 フレームを描画します。
    /// </summary>
    internal sealed class Preview3DRenderer : IDisposable
    {
        private static readonly Color4 BackgroundColor = new(0.15f, 0.15f, 0.15f, 1f);

        private readonly GridRenderer grid = new();
        private readonly CameraGizmoRenderer cameraGizmo = new();
        private readonly FlatItemProvider flatItemProvider = new();
        private readonly ItemDrawContextBuilder contextBuilder = new();

        /// <summary>
        /// 独自の 3D 描画を持たないアイテムに使う既定のプロバイダー。
        /// </summary>
        public I3DProvider DefaultProvider => flatItemProvider;

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
            var projection = SceneCamera.GetProjectionMatrix((float)width / Math.Max(1, height));
            var render = new Render3DContext(device, context, viewPose.ViewMatrix, projection);

            grid.Draw(render, viewPose.Position);
            cameraGizmo.Draw(render, scene.SceneCameraPose);

            for (var i = 0; i < scene.Items.Count; i++)
                scene.Items[i].Provider.Draw(render, drawContexts[i]);
        }

        public void Dispose()
        {
            grid.Dispose();
            cameraGizmo.Dispose();
            flatItemProvider.Dispose();
            contextBuilder.Dispose();
        }
    }

    /// <summary>
    /// 1 フレーム分の描画に必要な、シーンの状態一式。
    /// </summary>
    internal sealed class PreviewScene
    {
        /// <summary>プレビューを見ている視点。</summary>
        public required CameraPose ViewPose { get; init; }

        /// <summary>ガイド表示するシーンカメラの姿勢。</summary>
        public required CameraPose SceneCameraPose { get; init; }

        /// <summary>タイムライン上の現在位置。</summary>
        public required FrameContext Time { get; init; }

        public required PreviewEnvironment Environment { get; init; }

        public required IReadOnlyList<PreviewItem> Items { get; init; }
    }
}
