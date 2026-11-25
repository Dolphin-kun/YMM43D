using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Plugin;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Effects.Flat3D
{
    internal sealed class Flat3DProcessor(Flat3DEffect effect, IGraphicsDevicesAndContext devices) : VideoEffect3DProcessorBase(effect, devices), I3DFlatSource
    {
        private static readonly Vector3[] Corners =
        [
            new(-0.5f, -0.5f, 0f),
            new(0.5f, -0.5f, 0f),
            new(-0.5f, 0.5f, 0f),
            new(0.5f, 0.5f, 0f),
        ];

        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines
            = new(device => new RenderPipeline<TransformConstants>(device, new PlaneMesh(device), new TextureMaterial(device)));

        public override bool ScalesToInputSize => false;

        protected override bool KeepsGroupMemberSolid => true;

        protected override bool ParentsGroupMembers => true;

        protected override Matrix4x4 GetGroupParentMatrix(in FrameContext time) => GetRotation(time);

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (TryDrawGroupMemberSolid(render, item))
                return;

            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null || !pipelines.TryGet(render.Device, out var pipeline))
                return;

            var time = EffectDescription is { } description ? FrameContext.FromItem(description) : item.Time;

            var world = GetLocalMatrix(time) * item.World;

            var constants = render.CreateConstants(world, item.Opacity, effect.IsUnlit, item.AlphaCutoff, effect.GetGloss(time));

            var settings = item.ToDrawSettings(FaceCulling.None, texture) with { SkipDepthWrite = !effect.WritesDepth };

            pipeline.Draw(render.Context, constants, settings);
        }

        public bool TryGetFlat(in FrameContext itemTime, ID3D11Device device, out Matrix4x4 local, out ID3D11ShaderResourceView? texture)
        {
            local = GetLocalMatrix(itemTime);
            texture = GetTexture(device);

            return texture is not null;
        }

        public Matrix4x4 GetFlatLocal(in FrameContext itemTime) => GetLocalMatrix(itemTime);

        private Matrix4x4 GetRotation(in FrameContext time)
            => Rotation3D.ForObject(-effect.RotationX.GetFloat(time), -effect.RotationY.GetFloat(time), -effect.RotationZ.GetFloat(time));

        private Matrix4x4 GetLocalMatrix(in FrameContext time)
        {
            var rotation = IsGroupEffect ? Matrix4x4.Identity : GetRotation(time);

            if (!TryGetSize(out var size, out var offset))
                return rotation;

            return WorldScale.CreateSizeMatrix(size, offset + size / 2f, rotation);
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime) => WorldBounds.FromPoints(Corners, GetLocalMatrix(itemTime));

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
