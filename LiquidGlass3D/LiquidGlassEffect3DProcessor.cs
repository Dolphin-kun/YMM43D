using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace LiquidGlass3D
{
    internal sealed class LiquidGlassEffect3DProcessor(LiquidGlassEffect3D effect, IGraphicsDevicesAndContext devices) : VideoEffect3DProcessorBase(effect, devices)
    {
        private const string FieldPrelude = "#define GLASS_FIELD 1";
        private const int FieldSlot = 10;
        private const int NormalSlot = 11;
        private const float BoundsMargin = 1.6f;

        private static readonly WorldBounds UnitBox = new(new Vector3(-0.5f), new Vector3(0.5f));
        private static readonly WorldBounds UnitPlane = new(new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f));
        private static readonly WorldBounds CaptureBox = new(new Vector3(-BoundsMargin / 2f), new Vector3(BoundsMargin / 2f));

        private readonly LiquidGlassEffect3D effect = effect;
        private readonly DeviceResourceCache<FieldResources> resources = new(device => new FieldResources(device));

        protected override bool KeepsGroupMemberSolid => true;

        protected override bool UsesGroupMemberBounds => false;

        protected override bool AcceptsOwnSolid(I3DProvider solid, in FrameContext itemTime) => true;

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            var time = EffectDescription is { } description ? FrameContext.FromItem(description) : item.Time;

            if (GroupMemberSolid is not { } solid)
            {
                DrawFlat(render, item, shared);
                return;
            }

            if (Place(solid, time) is not { } shape)
            {
                TryDrawGroupMemberSolid(render, item);
                return;
            }

            if (render.IsShadowPass || render.IsCapturingScene)
                return;

            var context = render.Context;

            if (!shared.Field.Bake(context, shape.Geometry, shape.Layout)
                || shared.Field.Distances is not { } distances
                || shared.Field.Normals is not { } normals)
            {
                return;
            }

            var world = shape.Layout.BoxMatrix * shape.Local * item.World;

            if (!Matrix4x4.Invert(world, out var inverse))
                return;

            var front = SceneCapture.NearDepth(UnitBox, world, render.View);
            var captured = item.DepthOnly ? null : shared.Capture.Capture(render, [this, effect, solid], front);
            var look = GlassLook.From(effect, time);
            var constants = look.ToConstants(shape.Layout.Size, 0f, Vector3.Transform(render.GetCameraPosition(), inverse), 0f, captured, shape.Layout.CellSize);
            var scene = render.CreateConstants(world, item.Opacity, alphaCutoff: 0f, gloss: look.Gloss);
            var sampler = RenderStates.For(render.Device).LinearSampler;
            var settings = item.ToDrawSettings(FaceCulling.Front, captured?.View) with { Sampler = sampler };

            context.PSSetShaderResource(FieldSlot, distances);
            context.PSSetShaderResource(NormalSlot, normals);
            context.PSSetSampler(0, sampler);

            try
            {
                shared.Pipeline.Draw(context, scene, constants, settings, shared.Mesh);
            }
            finally
            {
                context.PSSetShaderResource(FieldSlot, null!);
                context.PSSetShaderResource(NormalSlot, null!);
            }
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            if (GroupMemberSolid is not { } solid)
                return UnitPlane;

            if (Place(solid, itemTime) is { } shape)
                return CaptureBox.Transform(shape.Layout.BoxMatrix * shape.Local);

            return solid is I3DBounds bounded ? bounded.GetLocalBounds(itemTime) : WorldBounds.Empty;
        }

        private void DrawFlat(in Render3DContext render, DrawContext3D item, FieldResources shared)
        {
            if ((item.Texture ?? GetTexture(render.Device)) is not { } texture)
                return;

            var constants = render.CreateConstants(item.World, item.Opacity, alphaCutoff: item.AlphaCutoff);

            shared.Plane.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.None, texture));
        }

        private Placed? Place(I3DProvider solid, in FrameContext time)
        {
            if (solid is not I3DGeometrySource source
                || !source.TryGetGeometry(time, out var geometry, out _, out var local)
                || MeshDistanceField.IsOpen(geometry))
            {
                return null;
            }

            return DistanceFieldLayout.Measure(geometry.Vertices, PixelScale(local), effect.Resolution) is { } layout ? new Placed(geometry, local, layout) : null;
        }

        private static Vector3 PixelScale(in Matrix4x4 local)
        {
            var x = new Vector3(local.M11, local.M12, local.M13).Length();
            var y = new Vector3(local.M21, local.M22, local.M23).Length();
            var z = new Vector3(local.M31, local.M32, local.M33).Length();

            return new Vector3(x, y, z) * WorldScale.ToPixels(1f);
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private readonly record struct Placed(SurfaceGeometry Geometry, Matrix4x4 Local, DistanceFieldLayout Layout);

        private sealed class FieldResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Pipeline { get; } = new(device, Vertex.InputElements, new ShaderMaterial(device, typeof(FieldResources).Assembly, "LiquidGlass3D.hlsl", FieldPrelude));

            public BoxMesh Mesh { get; } = BoxMesh.CreateUnitCube(device);

            public SceneCapture Capture { get; } = new(device);

            public MeshDistanceField Field { get; } = new(device);

            public RenderPipeline<TransformConstants> Plane { get; } = new(device, new PlaneMesh(device), new TextureMaterial(device));

            public void Dispose()
            {
                Plane.Dispose();
                Field.Dispose();
                Capture.Dispose();
                Mesh.Dispose();
                Pipeline.Dispose();
            }
        }
    }
}
