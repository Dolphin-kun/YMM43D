using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Preview.ViewModels;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Preview.Views
{
    public partial class Preview3DView : UserControl
    {
        private Point lastMousePos;
        private bool isRotating;
        private bool isPanning;

        private Preview3DViewModel? freeCameraOwner;
        private bool hasFreeCameraState;
        private float freeYaw;
        private float freePitch;
        private float freeRoll;
        private float freeDistance = 10f;
        private Vector3 freeTarget = Vector3.Zero;

        private YMM43D.Commons.DeviceResourceCache<GridResources>? gridCache;
        private YMM43D.Commons.DeviceResourceCache<CameraResources>? cameraCache;

        private D3D11Host? D3DHost;

        public Preview3DView()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                EnsureRenderCaches();
                if (D3DHost != null) return;
                D3DHost = new D3D11Host();
                RootGrid.Children.Add(D3DHost);
                D3DHost.Render += OnRenderTarget;
                D3DHost.MouseAction += OnMouseAction;
                CompositionTarget.Rendering += OnRendering;
            };
            Unloaded += (s, e) =>
            {
                CompositionTarget.Rendering -= OnRendering;
                if (D3DHost != null)
                {
                    D3DHost.Render -= OnRenderTarget;
                    D3DHost.MouseAction -= OnMouseAction;
                    RootGrid.Children.Remove(D3DHost);
                    D3DHost.Dispose();
                    D3DHost = null;
                }

                DisposeRenderCaches();
                if (DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            };
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (D3DHost == null) return;

            // 毎フレーム検知することで、プロパティ通知に頼らずアイテム増減に対応
            if (DataContext is Preview3DViewModel vm)
            {
                vm.RefreshOutputPreviewIfCameraChanged();
                vm.UpdatePreviewTarget();
            }

            D3DHost.RenderFrame();
        }

        private void OnRenderTarget(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            if (context == null || D3DHost == null) return;

            var rtv = D3DHost.RenderTargetView;
            var dsv = D3DHost.DepthStencilView;

            if (rtv == null || dsv == null) return;

            context.OMSetRenderTargets(rtv, dsv);
            context.ClearRenderTargetView(rtv, new Color4(0.15f, 0.15f, 0.15f, 1.0f));
            context.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1.0f, 0);
            context.RSSetViewport(new Viewport(0, 0, width, height));

            if (DataContext is not Preview3DViewModel vm) return;

            var frame = vm.CurrentFrame;
            var length = vm.TimelineSourceDescription?.TimelineDuration.Frame ?? 0;
            var fps = vm.FPS;

            EnsureFreeCameraState(vm, frame, length, fps);

            var rotation = Commons.Math.CreateCameraRotation(freeYaw, freePitch, freeRoll);
            var lookDir = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            var cameraPos = freeTarget - lookDir * freeDistance;

            var view = Matrix4x4.CreateLookAt(cameraPos, freeTarget, Vector3.Transform(Vector3.UnitY, rotation));
            var proj = vm.FreeCamera.GetProjectionMatrix((float)width / height);

            DrawGrid(context, device, view, proj, cameraPos);
            DrawCamera(context, device, view, proj, vm.SceneCamera, frame, length, fps);

            foreach (var previewItem in vm.PreviewItems)
            {
                if (previewItem.Provider == null) continue;

                int itemFrame = frame - previewItem.ItemFrame;
                int itemLength = previewItem.ItemLength;

                var drawContext = PropertyMapper.Map(previewItem.Item, itemFrame, itemLength, fps, device, vm.Scene, vm.TimelineSourceDescription, previewItem.Provider.RequiresMappedTexture);

                previewItem.Provider.Draw(device, context, view, proj, drawContext);
                if (drawContext.OwnsTexture)
                    drawContext.Texture?.Dispose();
            }
        }

        private void DrawGrid(ID3D11DeviceContext context, ID3D11Device device, Matrix4x4 view, Matrix4x4 proj, Vector3 cameraPos)
        {
            var cache = gridCache;
            if (cache == null) return;
            var res = cache.Get(device);

            var data = new GridResources.ConstantData
            {
                WorldViewProjection = Matrix4x4.Transpose(view * proj),
                CameraPos = new Vector4(cameraPos, 0)
            };
            context.UpdateSubresource(in data, res.ConstantBuffer);

            context.OMSetBlendState(res.BlendState);
            context.RSSetState(res.RasterizerState);

            context.VSSetShader(res.Material.VertexShader);
            context.PSSetShader(res.Material.PixelShader);
            context.IASetInputLayout(res.InputLayout);
            context.IASetVertexBuffer(0, res.Geometry.VertexBuffer, System.Runtime.InteropServices.Marshal.SizeOf<Vertex>(), 0);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
            context.VSSetConstantBuffer(0, res.ConstantBuffer);
            context.PSSetConstantBuffer(0, res.ConstantBuffer);

            context.Draw(res.Geometry.VertexCount, 0);

            context.OMSetBlendState(null);
            context.RSSetState(null);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        }

        private void OnMouseAction(Point pos, D3D11Host.MouseEventKind kind, int delta)
        {
            if (DataContext is not Preview3DViewModel vm) return;

            int frame = vm.CurrentFrame;
            int length = vm.TimelineSourceDescription?.TimelineDuration.Frame ?? 0;
            int fps = vm.FPS;
            EnsureFreeCameraState(vm, frame, length, fps);

            switch (kind)
            {
                case D3D11Host.MouseEventKind.Down:
                    lastMousePos = pos;
                    isRotating = true;
                    break;
                case D3D11Host.MouseEventKind.RightDown:
                    lastMousePos = pos;
                    isPanning = true;
                    break;
                case D3D11Host.MouseEventKind.Up:
                case D3D11Host.MouseEventKind.RightUp:
                    isRotating = false;
                    isPanning = false;
                    CommitFreeCameraState(vm);
                    break;
                case D3D11Host.MouseEventKind.Move:
                    var diff = pos - lastMousePos;
                    lastMousePos = pos;
                    if (isRotating)
                    {
                        freeYaw -= (float)diff.X * 0.5f;
                        freePitch = Math.Clamp(freePitch - (float)diff.Y * 0.5f, -89.9f, 89.9f);
                    }
                    else if (isPanning)
                    {
                        var rotation = YMM43D.Commons.Math.CreateCameraRotation(freeYaw, freePitch, freeRoll);
                        var right = Vector3.Transform(Vector3.UnitX, rotation);
                        var up = Vector3.Transform(Vector3.UnitY, rotation);
                        freeTarget += right * (float)-diff.X * freeDistance * 0.0015f;
                        freeTarget += up * (float)diff.Y * freeDistance * 0.0015f;
                    }
                    break;
                case D3D11Host.MouseEventKind.Wheel:
                    freeDistance = Math.Max(0.1f, freeDistance - delta * 0.005f);
                    CommitFreeCameraState(vm);
                    break;
            }
        }

        private void EnsureFreeCameraState(Preview3DViewModel vm, int frame, int length, int fps)
        {
            if (!ReferenceEquals(freeCameraOwner, vm))
            {
                freeCameraOwner = vm;
                hasFreeCameraState = false;
            }

            if (hasFreeCameraState)
                return;

            var camera = vm.FreeCamera;
            freeYaw = (float)camera.CameraYaw.GetValue(frame, length, fps);
            freePitch = (float)camera.CameraPitch.GetValue(frame, length, fps);
            freeRoll = (float)camera.CameraRoll.GetValue(frame, length, fps);
            freeDistance = (float)camera.CameraDistance.GetValue(frame, length, fps);
            freeTarget = camera.CameraTarget;
            hasFreeCameraState = true;
        }

        private void CommitFreeCameraState(Preview3DViewModel vm)
        {
            if (!hasFreeCameraState)
                return;

            var camera = vm.FreeCamera;
            camera.CameraYaw.CopyFrom(new Animation(freeYaw, -3600, 3600));
            camera.CameraPitch.CopyFrom(new Animation(freePitch, -90, 90));
            camera.CameraRoll.CopyFrom(new Animation(freeRoll, -3600, 3600));
            camera.CameraDistance.CopyFrom(new Animation(freeDistance, 0.1, 1000));
            camera.CameraTarget = freeTarget;
        }

        private void DrawCamera(ID3D11DeviceContext context, ID3D11Device device, Matrix4x4 view, Matrix4x4 proj, SceneCamera sceneCamera, int frame, int length, int fps)
        {
            var cache = cameraCache;
            if (cache == null) return;
            var res = cache.Get(device);

            var rotation = Commons.Math.CreateCameraRotation(
                (float)sceneCamera.CameraYaw.GetValue(frame, length, fps),
                (float)sceneCamera.CameraPitch.GetValue(frame, length, fps),
                (float)sceneCamera.CameraRoll.GetValue(frame, length, fps)
            );
            var lookDir = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            var cameraPos = sceneCamera.CameraTarget - lookDir * (float)sceneCamera.CameraDistance.GetValue(frame, length, fps);
            var world = rotation * Matrix4x4.CreateTranslation(cameraPos);
            var wvp = world * view * proj;
            var data = Matrix4x4.Transpose(wvp);
            context.UpdateSubresource(in data, res.ConstantBuffer);

            context.VSSetShader(res.VertexShader);
            context.PSSetShader(res.PixelShader);
            context.IASetInputLayout(res.InputLayout);
            context.IASetVertexBuffer(0, res.VertexBuffer, Marshal.SizeOf<Vertex>(), 0);
            context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
            context.VSSetConstantBuffer(0, res.ConstantBuffer);
            context.PSSetConstantBuffer(0, res.ConstantBuffer);

            context.Draw(res.VertexCount, 0);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        }

        private void EnsureRenderCaches()
        {
            gridCache ??= new Commons.DeviceResourceCache<GridResources>(device => new GridResources(device));
            cameraCache ??= new Commons.DeviceResourceCache<CameraResources>(device => new CameraResources(device));
        }

        private void DisposeRenderCaches()
        {
            gridCache?.Dispose();
            gridCache = null;
            cameraCache?.Dispose();
            cameraCache = null;
        }
    }
}
