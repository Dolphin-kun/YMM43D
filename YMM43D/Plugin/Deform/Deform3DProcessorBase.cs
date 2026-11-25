using System.Numerics;
using System.Reflection;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Plugin
{
    public abstract class Deform3DProcessorBase<TConstants> : VideoEffect3DProcessorBase where TConstants : unmanaged
    {
        private readonly DeviceResourceCache<DeformResources> resources;

        protected Deform3DProcessorBase(VideoEffect3DBase owner, IGraphicsDevicesAndContext devices) : base(owner, devices)
        {
            var assembly = GetType().Assembly;

            resources = new DeviceResourceCache<DeformResources>(device => new DeformResources(device, assembly, ShaderName));
        }

        protected abstract string ShaderName { get; }

        protected abstract bool IsUnlit { get; }

        protected abstract DeformGrid GetGrid(in FrameContext time);

        protected abstract TConstants GetConstants(in FrameContext time);

        protected abstract DeformExtent GetExtent(in FrameContext time);

        protected virtual SurfaceGloss GetGloss(in FrameContext time) => SurfaceGloss.None;

        private float DepthScale => TryGetSize(out var size, out _) ? WorldScale.ToWorld(size.X) : 1f;

        protected override bool KeepsGroupMemberSolid => true;

        protected override bool UsesGroupMemberBounds => false;

        protected override bool AcceptsOwnSolid(I3DProvider solid, in FrameContext itemTime)
            => solid is I3DGeometrySource source && source.TryGetGeometry(itemTime, out _, out _, out _);

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var time = EffectDescription is { } description ? FrameContext.FromItem(description) : item.Time;

            if (resources.TryGet(render.Device, out var ready)
                && GroupMemberSolid is I3DGeometrySource provider
                && provider.TryGetGeometry(time, out var geometry, out var colors, out var local))
            {
                var mesh = ready.GetSolidMesh(geometry, colors, GetGrid(time));
                var solidWorld = mesh.Fit * local * item.World;

                ready.SolidPipeline.Draw(
                    render.Context,
                    render.CreateConstants(solidWorld, item, IsUnlit, GetGloss(time)),
                    GetConstants(time),
                    item.ToDrawSettings(FaceCulling.None),
                    mesh);

                return;
            }

            if (GroupMemberSolid is I3DFlatSource flat
                && flat.TryGetFlat(time, render.Device, out var flatLocal, out var flatTexture)
                && resources.TryGet(render.Device, out var flatReady))
            {
                flatReady.Pipeline.Draw(
                    render.Context,
                    render.CreateConstants(Bent(flatLocal) * item.World, item, IsUnlit, GetGloss(time)),
                    GetConstants(time),
                    item.ToDrawSettings(FaceCulling.None, flatTexture),
                    flatReady.GetMesh(GetGrid(time)));

                return;
            }

            if (TryDrawGroupMemberSolid(render, item))
                return;

            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null || !resources.TryGet(render.Device, out var shared))
                return;

            var world = Matrix4x4.CreateScale(1f, 1f, DepthScale) * item.World;

            var scene = render.CreateConstants(world, item, IsUnlit, GetGloss(time));
            var constants = GetConstants(time);
            var settings = item.ToDrawSettings(FaceCulling.None, texture);

            shared.Pipeline.Draw(render.Context, scene, constants, settings, shared.GetMesh(GetGrid(time)));
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            var extent = GetExtent(itemTime);

            var half = 0.5f + MathF.Max(extent.Margin, 0f);

            if (GroupMemberSolid is I3DGeometrySource provider
                && provider.TryGetGeometry(itemTime, out var geometry, out _, out var local))
            {
                var (center, span) = DeformSolidMesh.Measure(geometry.Vertices);

                return new WorldBounds(new Vector3(-half), new Vector3(half)).Transform(Matrix4x4.CreateScale(span) * Matrix4x4.CreateTranslation(center) * local);
            }

            if (GroupMemberSolid is I3DFlatSource flat)
            {
                var reach = MathF.Max(extent.Depth, 0f);

                return new WorldBounds(new Vector3(-half, -half, -reach), new Vector3(half, half, reach)).Transform(Bent(flat.GetFlatLocal(itemTime)));
            }

            if (GroupMemberSolid is I3DBounds bounded)
                return bounded.GetLocalBounds(itemTime);

            var depth = MathF.Max(extent.Depth, 0f) * DepthScale;

            return new WorldBounds(new Vector3(-half, -half, -depth), new Vector3(half, half, depth));
        }

        private static Matrix4x4 Bent(in Matrix4x4 flatLocal)
            => Matrix4x4.CreateScale(1f, 1f, new Vector3(flatLocal.M11, flatLocal.M12, flatLocal.M13).Length()) * flatLocal;

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        private sealed class DeformResources(ID3D11Device device, Assembly assembly, string shader) : IDisposable
        {
            private const string SolidPrelude = "#define DEFORM_SOLID 1";

            private DeformMesh? mesh;
            private DeformSolidMesh? solid;
            private SurfaceGeometry? builtGeometry;
            private Color4[] builtColors = [];
            private DeformGrid builtGrid;

            public RenderPipeline<TransformConstants> Pipeline { get; } = new(device, DeformVertex.InputElements, new ShaderMaterial(device, assembly, shader));

            public RenderPipeline<TransformConstants> SolidPipeline { get; } = new(device, DeformSolidVertex.InputElements, new ShaderMaterial(device, assembly, shader, SolidPrelude));

            public DeformSolidMesh GetSolidMesh(SurfaceGeometry geometry, IReadOnlyList<Color4> colors, in DeformGrid grid)
            {
                if (solid is { } existing
                    && ReferenceEquals(builtGeometry, geometry)
                    && builtGrid == grid
                    && builtColors.AsSpan().SequenceEqual([.. colors]))
                {
                    return existing;
                }

                solid?.Dispose();

                builtGeometry = geometry;
                builtColors = [.. colors];
                builtGrid = grid;

                return solid = new DeformSolidMesh(device, geometry, colors, grid);
            }

            public DeformMesh GetMesh(in DeformGrid grid)
            {
                if (mesh is { } existing && existing.Grid == grid)
                    return existing;

                mesh?.Dispose();

                return mesh = new DeformMesh(device, grid);
            }

            public void Dispose()
            {
                mesh?.Dispose();
                mesh = null;
                solid?.Dispose();
                solid = null;
                SolidPipeline.Dispose();
                Pipeline.Dispose();
            }
        }
    }
}
