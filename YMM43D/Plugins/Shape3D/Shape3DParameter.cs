using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Rendering;
using YMM43D.Rendering.Geometries;
using YMM43D.Rendering.Materials;
using YMM43D.Rendering.States;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;
using YmmBlend = YukkuriMovieMaker.Project.Blend;

namespace YMM43D.Plugins.Shape3D
{
    /// <summary>
    /// Shape3DParameter 自体に I3DProvider を実装させることで、
    /// YMM4 のメインレンダラーが実行される前でも 3D プレビューにアイテムが表示されるようにする。
    /// </summary>
    internal class Shape3DParameter : ShapeParameterBase, I3DProvider
    {
        [Display(GroupName = "", Name = "サイズ")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation Size { get; } = new Animation(100, 0, 100000);

        [Display(GroupName = "", Name = "投影方法")]
        [EnumComboBox]
        public ProjectionType Projection { get => projection; set => Set(ref projection, value); }
        ProjectionType projection;

        [Display(GroupName = "3D回転", Name = "X")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RX { get; } = new Animation(0, -100000, 100000);

        [Display(GroupName = "3D回転", Name = "Y")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RY { get; } = new Animation(0, -100000, 100000);

        [Display(GroupName = "3D回転", Name = "Z")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation RZ { get; } = new Animation(0, -100000, 100000);

        // リソースキャッシュは Shape3DParameter が I3DProvider として描画する際に使用
        private readonly DeviceResourceCache<CubeResources> resourceCache = new(device => new CubeResources(device));

        public Shape3DParameter(SharedDataStore? sharedData) : base(sharedData)
        {
            // このコンストラクタが呼ばれた時点で自分自身を登録する。
            // これにより YMM4 のメインレンダラーが動作する前でも 3D プレビューに表示可能になる。
            ProviderRegistry.Register(this, this);
        }

        public Shape3DParameter() : this(null)
        {
        }

        // --- I3DProvider 実装 ---

        public void Draw(ID3D11DeviceContext context, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            var res = resourceCache.Get(context.Device);

            var rx = (float)(RX.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS) * Math.PI / 180.0);
            var ry = (float)(RY.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS) * Math.PI / 180.0);
            var rz = (float)(RZ.GetValue(drawContext.Frame, drawContext.Length, drawContext.FPS) * Math.PI / 180.0);
            var localRotation = Matrix4x4.CreateRotationX(rx) * Matrix4x4.CreateRotationY(ry) * Matrix4x4.CreateRotationZ(rz);

            var finalWorld = localRotation * drawContext.World;
            DrawCube(context, finalWorld * view, projection, drawContext.Opacity, drawContext.Blend,
                     drawContext.IsInverted, drawContext.IsAlwaysOnTop, drawContext.IsZOrderEnabled, res);
        }

        private static void DrawCube(ID3D11DeviceContext d3dDc, Matrix4x4 viewWorld, Matrix4x4 proj, float opacity,
            YmmBlend blend, bool inverted, bool alwaysOnTop, bool zOrder, CubeResources res)
        {
            var wvpMatrix = viewWorld * proj;
            var data = new CubeResources.ConstantData
            {
                WorldViewProjection = Matrix4x4.Transpose(wvpMatrix),
                Opacity = opacity
            };
            d3dDc.UpdateSubresource(in data, res.ConstantBuffer);

            var depthStates = res.DepthStencilStates.Get(d3dDc.Device);
            d3dDc.OMSetDepthStencilState(alwaysOnTop ? depthStates.NoDepth : depthStates.Default);

            var blendStates = res.BlendStates.Get(d3dDc.Device);
            d3dDc.OMSetBlendState(blend switch
            {
                YmmBlend.Add => blendStates.Add,
                YmmBlend.Subtract => blendStates.Subtract,
                YmmBlend.Multiply => blendStates.Multiply,
                YmmBlend.Screen => blendStates.Screen,
                _ => blendStates.Normal
            });

            d3dDc.VSSetShader(res.Material.VertexShader);
            d3dDc.PSSetShader(res.Material.PixelShader);
            d3dDc.IASetInputLayout(res.InputLayout);
            d3dDc.IASetVertexBuffer(0, res.Geometry.VertexBuffer, Marshal.SizeOf<Vertex>(), 0);
            d3dDc.IASetIndexBuffer(res.Geometry.IndexBuffer, Vortice.DXGI.Format.R16_UInt, 0);
            d3dDc.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            d3dDc.VSSetConstantBuffer(0, res.ConstantBuffer);
            d3dDc.PSSetConstantBuffer(0, res.ConstantBuffer);

            var rasterStates = res.RasterizerStates.Get(d3dDc.Device);
            d3dDc.RSSetState(rasterStates.CullFront);
            d3dDc.DrawIndexed(res.Geometry.IndexCount, 0, 0);
            d3dDc.RSSetState(rasterStates.CullBack);
            d3dDc.DrawIndexed(res.Geometry.IndexCount, 0, 0);

            d3dDc.OMSetBlendState(null);
            d3dDc.OMSetDepthStencilState(null);
            d3dDc.RSSetState(null);
        }

        // --- ShapeParameterBase 実装 ---

        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc)
            => [];

        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc)
            => [];

        public override IShapeSource CreateShapeSource(IGraphicsDevicesAndContext devices)
        {
            // Shape3DSource を作り直す際、自分自身の登録も更新する
            ProviderRegistry.Register(this, this);
            SharedGraphics.Devices = devices;
            return new Shape3DSource(devices, this);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Size, RX, RY, RZ];

        protected override void LoadSharedData(SharedDataStore store)
        {
            var sharedData = store.Load<SharedData>();
            if (sharedData is null) return;
            sharedData.CopyTo(this);
        }

        protected override void SaveSharedData(SharedDataStore store)
        {
            store.Save(new SharedData(this));
        }

        class SharedData
        {
            public Animation Size { get; } = new Animation(100, 0, 1000);
            public Animation RX { get; } = new Animation(0, -360, 360);
            public Animation RY { get; } = new Animation(0, -360, 360);
            public Animation RZ { get; } = new Animation(0, -360, 360);
            public ProjectionType Projection { get; set; }

            public SharedData(Shape3DParameter param)
            {
                Size.CopyFrom(param.Size);
                RX.CopyFrom(param.RX);
                RY.CopyFrom(param.RY);
                RZ.CopyFrom(param.RZ);
                Projection = param.Projection;
            }
            public void CopyTo(Shape3DParameter param)
            {
                param.Size.CopyFrom(Size);
                param.RX.CopyFrom(RX);
                param.RY.CopyFrom(RY);
                param.RZ.CopyFrom(RZ);
                param.Projection = Projection;
            }
        }
    }
}
