using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace Reflection3D
{
    internal sealed class Reflection3DProcessor(Reflection3DEffect effect, IGraphicsDevicesAndContext devices) : VideoEffect3DProcessorBase(effect, devices)
    {
        private static readonly SurfaceGeometry PlaneGeometry = Primitives.Plane();

        private static readonly Vector3[] Corners =
        [
            new(-0.5f, -0.5f, 0f),
            new(0.5f, -0.5f, 0f),
            new(-0.5f, 0.5f, 0f),
            new(0.5f, 0.5f, 0f),
        ];

        private readonly DeviceResourceCache<ReflectionResources> resources = new(device => new ReflectionResources(device));

        protected override bool KeepsGroupMemberSolid => true;

        protected override bool AcceptsOwnSolid(I3DProvider solid, in FrameContext itemTime) => true;

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            var time = EffectDescription is { } description ? FrameContext.FromItem(description) : item.Time;
            var surface = Resolve(render.Device, item, time);

            DrawBase(render, item, surface, shared);

            if (render.IsShadowPass || render.IsCapturingScene || item.DepthOnly || surface is not { } found)
                return;

            var look = ReflectionLook.From(effect, time);

            shared.Reflection.Draw(
                render,
                found.Geometry,
                found.Local * item.World,
                found.Mask,
                item.Opacity,
                item.IsAlwaysOnTop,
                look,
                [this, effect, GroupMemberSolid]);
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
            => GroupMemberSolid is null ? WorldBounds.FromPoints(Corners, Matrix4x4.Identity) : WorldBounds.Empty;

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private Surface? Resolve(ID3D11Device device, DrawContext3D item, in FrameContext time)
        {
            if (GroupMemberSolid is I3DGeometrySource source && source.TryGetGeometry(time, out var geometry, out _, out var local))
                return new Surface(geometry, local, null, false);

            if (GroupMemberSolid is I3DFlatSource flat && flat.TryGetFlat(time, device, out var placed, out var image))
                return new Surface(PlaneGeometry, placed, image, false);

            if (GroupMemberSolid is null && (item.Texture ?? GetTexture(device)) is { } own)
                return new Surface(PlaneGeometry, Matrix4x4.Identity, own, true);

            return null;
        }

        private void DrawBase(in Render3DContext render, DrawContext3D item, Surface? surface, ReflectionResources shared)
        {
            if (surface is not { IsOwnPlane: true } own)
            {
                TryDrawGroupMemberSolid(render, item);
                return;
            }

            var constants = render.CreateConstants(item.World, item.Opacity, alphaCutoff: item.AlphaCutoff);

            shared.Plane.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.None, own.Mask));
        }

        private readonly record struct Surface(SurfaceGeometry Geometry, Matrix4x4 Local, ID3D11ShaderResourceView? Mask, bool IsOwnPlane);

        private sealed class ReflectionResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Plane { get; } = new(device, new PlaneMesh(device), new TextureMaterial(device));

            public ReflectionRenderer Reflection { get; } = new(device);

            public void Dispose()
            {
                Reflection.Dispose();
                Plane.Dispose();
            }
        }
    }
}
