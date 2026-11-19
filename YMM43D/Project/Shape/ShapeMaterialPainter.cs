using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics.Models;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Shape
{
    internal sealed class ShapeMaterialPainter : IDisposable
    {
        private readonly DeviceResourceCache<Resources> resources = new(device => new Resources(device));

        public void Draw(in Render3DContext render, DrawContext3D item, Matrix4x4 world, SurfaceGeometry geometry, IReadOnlyList<ShapeMaterial> materials, bool unlit)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            var time = item.Time;
            Color4[] colors = [.. materials.Select(material => material.Color.ToColor4())];
            var meshes = shared.GetMeshes(geometry, colors);
            var states = RenderStates.For(render.Device);
            var scene = render.Lighting?.Mirror ?? SceneMirror.None;
            var (mirrorView, mirrorLevels) = scene.HasImage ? shared.Images.Get(render.Context, scene.Path) : (null, 0f);

            for (var slot = 0; slot < meshes.Length; slot++)
            {
                if (meshes[slot] is not { } mesh)
                    continue;

                var material = materials[Math.Min(slot, materials.Count - 1)];
                var (color, _) = shared.Images.Get(render.Context, material.Image);
                var (normal, _) = shared.Images.Get(render.Context, material.NormalImage);
                var paint = new ModelPaint
                {
                    UvScale = new Vector2(material.ImageScaleX.GetFloat(time), material.ImageScaleY.GetFloat(time)) / 100f,
                    UvOffset = new Vector2(material.ImageOffsetX.GetFloat(time), material.ImageOffsetY.GetFloat(time)) / 100f,
                    UvRotation = float.DegreesToRadians(material.ImageRotation.GetFloat(time)),
                    Mirror = mirrorView is null ? 0f : 1f,
                };
                var glow = material.Emission;
                var surface = new ModelSurface
                {
                    Metallic = material.Metallic.GetFloat(time) / 100f,
                    Roughness = material.Roughness.GetFloat(time) / 100f,
                    Glow = new Vector3(glow.R, glow.G, glow.B) / 255f * (material.EmissionStrength.GetFloat(time) / 100f),
                    BumpScale = material.NormalStrength.GetFloat(time) / 100f,
                };
                var gloss = unlit ? SurfaceGloss.None : SurfaceGloss.FromPercent(material.Gloss.GetFloat(time), material.GlossSharpness.GetFloat(time));
                var constants = render.CreateConstants(world, item.Opacity, unlit, item.AlphaCutoff, gloss, paint);
                var extra = ModelConstants.Create(surface, paint, new ModelConstants.Loaded(normal is not null, false, false, false, mirrorView is not null), mirrorLevels, scene.Ambient);

                foreach (var culling in new[] { FaceCulling.Front, FaceCulling.Back })
                {
                    var settings = item.ToDrawSettings(culling, color ?? shared.Images.White(render.Context)) with
                    {
                        Sampler = states.LinearWrapSampler,
                        Bump = normal,
                        Mirror = mirrorView,
                    };

                    shared.Pipeline.Draw(render.Context, constants, extra, settings, mesh);
                }
            }
        }

        public void Dispose() => resources.Dispose();

        private sealed class Resources(ID3D11Device device) : IDisposable
        {
            private SurfaceGeometry? builtGeometry;
            private Color4[] builtColors = [];
            private MaterialSlotMesh?[] meshes = [];

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(device, Vertex.InputElements, new ModelMaterial(device));

            public ImageTextures Images { get; } = new(device);

            public MaterialSlotMesh?[] GetMeshes(SurfaceGeometry geometry, Color4[] colors)
            {
                if (ReferenceEquals(builtGeometry, geometry) && builtColors.SequenceEqual(colors))
                    return meshes;

                Drop();
                builtGeometry = geometry;
                builtColors = colors;
                meshes = MaterialSlotMesh.Build(device, geometry, colors);

                return meshes;
            }

            private void Drop()
            {
                foreach (var mesh in meshes)
                    mesh?.Dispose();

                meshes = [];
            }

            public void Dispose()
            {
                Drop();
                Images.Dispose();
                Pipeline.Dispose();
            }
        }
    }
}
