using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Models;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Model
{
    internal sealed class Model3DSource(IGraphicsDevicesAndContext devices, Model3DParameter parameter)
        : Shape3DSourceBase(devices)
    {
        private readonly Model3DParameter parameter = parameter;
        private readonly DeviceResourceCache<ModelResources> resources = new(device => new ModelResources(device));

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (parameter.Model is not { } model)
                return;

            var world = parameter.GetLocalMatrix(model, item.Time) * item.World;
            var constants = render.CreateConstants(
                world, item.Opacity, parameter.IsUnlit, item.AlphaCutoff, parameter.GetGloss(item.Time));

            var shared = resources.Get(render.Device);
            var mesh = shared.GetMesh(render.Context, model, parameter.Tint.ToVector4());
            var states = RenderStates.For(render.Device);

            foreach (var (part, texture) in mesh.Parts)
            {
                var settings = item.ToDrawSettings(FaceCulling.None, texture ?? shared.GetWhite(render.Context)) with
                {
                    Sampler = states.LinearWrapSampler,
                };

                shared.Pipeline.Draw(render.Context, constants, settings, part);
            }
        }

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            if (parameter.Model is not { } model)
                return WorldBounds.Empty;

            return model.Bounds.Transform(parameter.GetLocalMatrix(model, itemTime));
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private sealed class ModelResources : IDisposable
        {
            private readonly ID3D11Device device;

            private ModelMesh? mesh;
            private ID3D11Texture2D? whiteTexture;
            private ID3D11ShaderResourceView? white;

            public RenderPipeline<TransformConstants> Pipeline { get; }

            public ModelResources(ID3D11Device device)
            {
                this.device = device;

                Pipeline = new RenderPipeline<TransformConstants>(device, Vertex.InputElements, new TextureMaterial(device));
            }

            public ID3D11ShaderResourceView GetWhite(ID3D11DeviceContext context)
            {
                if (white is not null)
                    return white;

                whiteTexture = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = 1,
                    Height = 1,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                    SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                });

                context.UpdateSubresource(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, whiteTexture, 0, 4);

                return white = device.CreateShaderResourceView(whiteTexture);
            }

            public ModelMesh GetMesh(ID3D11DeviceContext context, ModelData model, Vector4 tint)
            {
                if (mesh is { } existing && ReferenceEquals(existing.Source, model) && existing.Tint == tint)
                    return existing;

                mesh?.Dispose();

                return mesh = new ModelMesh(device, context, model, tint);
            }

            public void Dispose()
            {
                mesh?.Dispose();
                mesh = null;
                white?.Dispose();
                white = null;
                whiteTexture?.Dispose();
                whiteTexture = null;
                Pipeline.Dispose();
            }
        }
    }
}
