using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics.Models;

namespace YMM43D.Project.Model
{
    internal interface IModelLook
    {
        ModelData? Model { get; }

        Vector4 Tone { get; }

        bool IsUnlit { get; }

        SurfaceGloss GetGloss(in FrameContext time);

        ModelPaint GetPaint(in FrameContext time);

        Matrix4x4 GetLocalMatrix(ModelData model, in FrameContext time);

        IReadOnlyList<ModelImage?> GetReplacements(ModelData model);

        IReadOnlyList<(ModelBlend Blend, float Cutoff)?> GetBlends(ModelData model);

        IReadOnlyList<Matrix4x4>? GetPose(
            ModelData model, in FrameContext time, in Matrix4x4 world, bool advance);

        IReadOnlyList<float>? GetMorphWeights(ModelData model, in FrameContext time);
    }

    internal sealed class ModelPainter : IDisposable
    {
        private readonly DeviceResourceCache<ModelResources> resources = new(device => new ModelResources(device));

        public void Draw(in Render3DContext render, DrawContext3D item, IModelLook look)
        {
            if (look.Model is not { } model || !resources.TryGet(render.Device, out var shared))
                return;

            var world = look.GetLocalMatrix(model, item.Time) * item.World;
            var gloss = look.GetGloss(item.Time);
            var paint = look.GetPaint(item.Time);

            var mesh = shared.GetMesh(render.Context, model, look.Tone, look.GetReplacements(model));
            var states = RenderStates.For(render.Device);

            var advance = render.IsOutput && !item.DepthOnly && !render.IsShadowPass;

            var bones = model.HasBones
                ? shared.WriteBones(render.Context, model, look.GetPose(model, item.Time, world, advance))
                : null;
            var morphs = shared.WriteMorphs(render.Context, model, look.GetMorphWeights(model, item.Time));
            var pipeline = model.IsDeformed ? shared.SkinnedPipeline : shared.Pipeline;

            var blends = look.GetBlends(model);

            var scene = render.Lighting?.Mirror ?? SceneMirror.None;
            var wanted = scene.HasImage && (paint.Mirror > 0f || scene.Ambient > 0f);

            var mirror = wanted
                ? shared.GetMirror(render.Context, scene.Path)
                : (View: (ID3D11ShaderResourceView?)null, Levels: 0f);

            foreach (var (shape, textures, part) in Ordered(mesh.Parts, blends, world, render.GetCameraPosition()))
            {
                var (blend, sharpness) = BlendOf(part, blends);

                if (blend == ModelBlend.Blend && (item.DepthOnly || render.IsShadowPass))
                    continue;

                var cutoff = blend == ModelBlend.Mask ? MathF.Max(sharpness, item.AlphaCutoff) : item.AlphaCutoff;

                var constants = render.CreateConstants(
                    world, item.Opacity, look.IsUnlit, cutoff, gloss, paint);

                var surface = ModelConstants.Create(
                    part.Surface,
                    paint,
                    new ModelConstants.Loaded(
                        textures.Bump is not null,
                        textures.Glow is not null,
                        textures.Feel is not null,
                        textures.Shade is not null,
                        mirror.View is not null),
                    mirror.Levels,
                    scene.Ambient);

                var settings = item.ToDrawSettings(
                    FaceCulling.None, textures.Color ?? shared.GetWhite(render.Context)) with
                {
                    Sampler = states.LinearWrapSampler,
                    Bones = bones,
                    Morphs = morphs,
                    Bump = textures.Bump,
                    Glow = textures.Glow,
                    Feel = textures.Feel,
                    Shade = textures.Shade,
                    Mirror = mirror.View,

                    SkipDepthWrite = blend == ModelBlend.Blend,
                };

                pipeline.Draw(render.Context, constants, surface, settings, shape);
            }
        }

        private static (ModelBlend Blend, float Cutoff) BlendOf(
            in ModelPart part, IReadOnlyList<(ModelBlend Blend, float Cutoff)?> chosen)
            => part.Material >= 0 && part.Material < chosen.Count && chosen[part.Material] is { } set
                ? set
                : (part.Blend, part.Cutoff);

        private static IEnumerable<(IMesh Mesh, ModelTextures Textures, ModelPart Part)> Ordered(
            IReadOnlyList<(IMesh Mesh, ModelTextures Textures, ModelPart Part)> parts,
            IReadOnlyList<(ModelBlend Blend, float Cutoff)?> chosen,
            Matrix4x4 world,
            Vector3 camera)
        {
            var solid = parts.Where(one => BlendOf(one.Part, chosen).Blend != ModelBlend.Blend);
            var clear = parts
                .Where(one => BlendOf(one.Part, chosen).Blend == ModelBlend.Blend)
                .OrderByDescending(one => Vector3.DistanceSquared(Vector3.Transform(one.Part.Center, world), camera));

            return solid.Concat(clear);
        }

        public void Dispose() => resources.Dispose();

        private sealed class ModelResources : IDisposable
        {
            private readonly ID3D11Device device;

            private ModelMesh? mesh;
            private ID3D11Texture2D? whiteTexture;
            private ID3D11ShaderResourceView? white;
            private RenderPipeline<TransformConstants>? skinnedPipeline;
            private ModelBoneBuffer? boneBuffer;
            private ModelMorphBuffer? morphBuffer;

            private string mirrorPath = string.Empty;
            private ID3D11Texture2D? mirrorTexture;
            private ID3D11ShaderResourceView? mirror;
            private float mirrorLevels;

            public RenderPipeline<TransformConstants> Pipeline { get; }

            public RenderPipeline<TransformConstants> SkinnedPipeline
                => skinnedPipeline ??= new RenderPipeline<TransformConstants>(
                    device, SkinnedVertex.InputElements, new SkinnedModelMaterial(device));

            public ModelResources(ID3D11Device device)
            {
                this.device = device;
                Pipeline = new RenderPipeline<TransformConstants>(
                    device, Vertex.InputElements, new ModelMaterial(device));
            }

            public ModelMesh GetMesh(
                ID3D11DeviceContext context, ModelData model, Vector4 tint, IReadOnlyList<ModelImage?> replacements)
            {
                if (mesh is { } existing
                    && ReferenceEquals(existing.Source, model)
                    && existing.Tint == tint
                    && existing.Replacements.SequenceEqual(replacements, ReferenceEqualityComparer.Instance))
                {
                    return existing;
                }

                mesh?.Dispose();

                return mesh = new ModelMesh(device, context, model, tint, replacements);
            }

            public (ID3D11ShaderResourceView? View, float Levels) GetMirror(
                ID3D11DeviceContext context, string path)
            {
                if (mirrorPath == path)
                    return (mirror, mirrorLevels);

                DropMirror();
                mirrorPath = path;

                if (path.Length == 0
                    || ModelLibrary.FindImage(path) is not { } image
                    || ModelTexture.Create(device, context, image) is not { } made)
                {
                    return (null, 0f);
                }

                mirrorTexture = made.Texture;
                mirror = made.View;
                mirrorLevels = made.Levels;

                return (mirror, mirrorLevels);
            }

            public ID3D11ShaderResourceView? WriteBones(
                ID3D11DeviceContext context, ModelData model, IReadOnlyList<Matrix4x4>? pose)
            {
                boneBuffer ??= new ModelBoneBuffer(device);
                boneBuffer.Write(context, ModelPose.Skin(model, pose));

                return boneBuffer.View;
            }

            public ID3D11ShaderResourceView? WriteMorphs(
                ID3D11DeviceContext context, ModelData model, IReadOnlyList<float>? weights)
            {
                if (!model.HasMorphs)
                    return null;

                morphBuffer ??= new ModelMorphBuffer(device);

                return morphBuffer.Write(context, model, weights);
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

            private void DropMirror()
            {
                mirror?.Dispose();
                mirror = null;
                mirrorTexture?.Dispose();
                mirrorTexture = null;
                mirrorLevels = 0f;
                mirrorPath = string.Empty;
            }

            public void Dispose()
            {
                mesh?.Dispose();
                mesh = null;
                white?.Dispose();
                white = null;
                whiteTexture?.Dispose();
                whiteTexture = null;
                DropMirror();
                skinnedPipeline?.Dispose();
                skinnedPipeline = null;
                boneBuffer?.Dispose();
                boneBuffer = null;
                morphBuffer?.Dispose();
                morphBuffer = null;
                Pipeline.Dispose();
            }
        }
    }
}
