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
using YukkuriMovieMaker.Resources.Icons;

namespace YMM43D.Preview.Views
{
    public class IconResourceConverter : System.Windows.Data.IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is not bool isVisible) return null;
            var key = isVisible ? IconKeys.MenuRight : IconKeys.MenuLeft;
            return Application.Current.TryFindResource(key);
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    public partial class Preview3DView : UserControl
    {
        private Point lastMousePos;
        private bool isRotating;
        private bool isPanning;
        private bool isInitialized = false;

        private readonly DeviceResourceCache<GridResources> gridCache = new(device => new GridResources(device));
        private readonly DeviceResourceCache<CameraResources> cameraCache = new(device => new CameraResources(device));

        public Preview3DView()
        {
            InitializeComponent();
            
            Loaded += (s, e) => CompositionTarget.Rendering += OnRendering;
            Unloaded += (s, e) => {
                CompositionTarget.Rendering -= OnRendering;
                gridCache.Dispose();
                cameraCache.Dispose();
                D3DHost.Dispose();
            };

            D3DHost.Render += OnRenderTarget;
            D3DHost.MouseAction += OnMouseAction;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!isInitialized)
            {
                D3DHost.InitializeIndependent();
                isInitialized = true;
            }

            // 毎フレーム検知することで、プロパティ通知に頼らずアイテム増減に対応
            if (DataContext is Preview3DViewModel vm)
            {
                vm.UpdatePreviewTarget();
            }

            D3DHost.RenderFrame();
        }

        private void OnRenderTarget(ID3D11DeviceContext context, int width, int height)
        {
            if (context == null) return;
            var rtv = D3DHost.RenderTargetView;
            var dsv = D3DHost.DepthStencilView;
            
            if (rtv == null || dsv == null) return;

            context.OMSetRenderTargets(rtv, dsv);
            context.ClearRenderTargetView(rtv, new Color4(0.15f, 0.15f, 0.15f, 1.0f));
            context.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1.0f, 0);
            context.RSSetViewport(new Viewport(0, 0, width, height));

            if (DataContext is not Preview3DViewModel vm) return;
            var camera = vm.ActiveCamera;

            var rotation = Matrix4x4.CreateRotationY(camera.CameraYaw) * Matrix4x4.CreateRotationX(camera.CameraPitch);
            var lookDir = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            var cameraPos = camera.CameraTarget - lookDir * camera.CameraDistance;
            
            var view = Matrix4x4.CreateLookAt(cameraPos, camera.CameraTarget, Vector3.Transform(Vector3.UnitY, rotation));
            var proj = Matrix4x4.CreatePerspectiveFieldOfView((float)Math.PI / 4, (float)width / height, 0.1f, 1000f);

            DrawGrid(context, view, proj, cameraPos);

            // 自由カメラ操作時は、出力用カメラ（SceneCamera）をワイヤーフレームとして描画する
            if (!vm.IsLockToCamera)
            {
                DrawCamera(context, view, proj, vm.SceneCamera);
            }

            foreach (var previewItem in vm.PreviewItems)
            {
                if (previewItem.Provider == null) continue;

                int currentFrame = vm.CurrentFrame;
                int frame = currentFrame - previewItem.ItemFrame;
                int length = previewItem.ItemLength;
                int fps = vm.FPS;

                var drawContext = PropertyMapper.Map(previewItem.Item, frame, length, fps, context.Device, vm.Scene, vm.TimelineSourceDescription, previewItem.Provider.RequiresMappedTexture);

                previewItem.Provider.Draw(context, view, proj, drawContext);
                if (drawContext.OwnsTexture)
                    drawContext.Texture?.Dispose();
            }
        }

        private void DrawGrid(ID3D11DeviceContext context, Matrix4x4 view, Matrix4x4 proj, Vector3 cameraPos)
        {
            var res = gridCache.Get(context.Device);
            
            var data = new GridResources.ConstantData {
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
            var camera = vm.ActiveCamera;

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
                    break;
                case D3D11Host.MouseEventKind.Move:
                    var diff = pos - lastMousePos;
                    lastMousePos = pos;
                    if (isRotating)
                    {
                        camera.CameraYaw -= (float)diff.X * 0.01f;
                        camera.CameraPitch -= (float)diff.Y * 0.01f;
                        camera.CameraPitch = Math.Clamp(camera.CameraPitch, -MathF.PI / 2 + 0.1f, MathF.PI / 2 - 0.1f);
                    }
                    else if (isPanning)
                    {
                        var rot = Matrix4x4.CreateRotationY(camera.CameraYaw) * Matrix4x4.CreateRotationX(camera.CameraPitch);
                        var right = Vector3.Transform(Vector3.UnitX, rot);
                        var up = Vector3.Transform(Vector3.UnitY, rot);
                        var target = camera.CameraTarget;
                        target += right * (float)-diff.X * camera.CameraDistance * 0.0015f;
                        target += up * (float)diff.Y * camera.CameraDistance * 0.0015f;
                        camera.CameraTarget = target;
                    }
                    break;
                case D3D11Host.MouseEventKind.Wheel:
                    camera.CameraDistance -= delta * 0.005f;
                    camera.CameraDistance = Math.Max(0.1f, camera.CameraDistance);
                    break;
            }
        }

        private void DrawCamera(ID3D11DeviceContext context, Matrix4x4 view, Matrix4x4 proj, SceneCamera sceneCamera)
        {
            var res = cameraCache.Get(context.Device);

            // カメラモデルの配置（SceneCameraのパラメータから逆算）
            var rotation = Matrix4x4.CreateRotationY(sceneCamera.CameraYaw) * Matrix4x4.CreateRotationX(sceneCamera.CameraPitch);
            var lookDir = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            var cameraPos = sceneCamera.CameraTarget - lookDir * sceneCamera.CameraDistance;
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
    }
}
