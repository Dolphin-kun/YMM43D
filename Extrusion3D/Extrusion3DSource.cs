using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Rendering;
using YMM43D.Commons;

namespace Extrusion3D
{
    internal class Extrusion3DSource(Extrusion3DEffect effect, Extrusion3DProcessor processor) : IDisposable
    {
        private readonly Extrusion3DEffect effect = effect;
        internal readonly Extrusion3DProcessor processor = processor;
        private readonly DeviceResourceCache<ExtrusionResources> resourceCache = new(device => new ExtrusionResources(device));

        // --- 3D 描画 ---

        public void Draw3D(ID3D11Device device, ID3D11DeviceContext d3dDc, Matrix4x4 view, Matrix4x4 projection, DrawContext3D drawContext)
        {
            var texture = drawContext.Texture ?? processor.GetTexture(device);
            if (texture == null) return;
            
            var res = resourceCache.Get(device);
            
            int frame = drawContext.Frame;
            int length = drawContext.Length;
            int fps = drawContext.FPS;

            if (processor.EffectDescription != null)
            {
                frame = processor.EffectDescription.ItemPosition.Frame;
                length = processor.EffectDescription.ItemDuration.Frame;
                fps = processor.EffectDescription.FPS;
            }
            
            float thickness = (float)(effect.Thickness.GetValue(frame, length, fps) / 100.0);
            if (thickness <= 0) return;

            var extrusionMatrix = Matrix4x4.CreateScale(1.0f, 1.0f, thickness);
            var finalWorld = extrusionMatrix * drawContext.World;
            var wvpMatrix = finalWorld * view * projection;

            var sideColor = effect.SideColor;
            var sideColorVec = new Vector4(sideColor.R / 255f, sideColor.G / 255f, sideColor.B / 255f, sideColor.A / 255f);

            Matrix4x4.Invert(view, out var invView);
            var cameraWorldPos = invView.Translation;
            Matrix4x4.Invert(finalWorld, out var invWorld);
            var cameraLocalPos = Vector3.Transform(cameraWorldPos, invWorld);

            var attenuation = (float)(effect.Attenuation.GetValue(frame, length, fps) / 100.0);

            var data = new ExtrusionResources.ConstantData
            {
                WorldViewProjection = Matrix4x4.Transpose(wvpMatrix),
                SideColor = sideColorVec,
                CameraLocalPos = cameraLocalPos,
                Opacity = drawContext.Opacity,
                ExtrusionType = (int)effect.ExtrusionType,
                Attenuation = attenuation
            };
            d3dDc.UpdateSubresource(in data, res.ConstantBuffer);

            // パイプラインステートの設定
            d3dDc.OMSetDepthStencilState(drawContext.IsAlwaysOnTop ? res.DepthStencilStates.NoDepth : res.DepthStencilStates.Default);
            d3dDc.OMSetBlendState(res.BlendStates.Normal);

            // シェーダーとジオメトリの設定
            d3dDc.VSSetShader(res.Material.VertexShader);
            d3dDc.PSSetShader(res.Material.PixelShader);
            d3dDc.IASetInputLayout(res.InputLayout);
            d3dDc.IASetVertexBuffer(0, res.Geometry.VertexBuffer, Marshal.SizeOf<Vertex>(), 0);
            d3dDc.IASetIndexBuffer(res.Geometry.IndexBuffer, Format.R16_UInt, 0);
            d3dDc.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            d3dDc.VSSetConstantBuffer(0, res.ConstantBuffer);
            d3dDc.PSSetConstantBuffer(0, res.ConstantBuffer);
            d3dDc.PSSetShaderResource(0, texture);
            d3dDc.PSSetSampler(0, res.SamplerState);

            // 描画コール
            d3dDc.RSSetState(res.RasterizerStates.CullFront);
            d3dDc.DrawIndexed(res.Geometry.IndexCount, 0, 0);

            // クリーンアップ
            d3dDc.RSSetState(null!);
            d3dDc.PSSetShaderResource(0, null!);
        }

        public void Dispose()
        {
            resourceCache.Dispose();
        }
    }
}
