using System.Numerics;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Player;
using YMM43D.Plugin;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Project.Effects.Flat3D
{
    internal sealed class Flat3DProcessor(Flat3DEffect effect, IGraphicsDevicesAndContext devices) : VideoEffect3DProcessorBase(effect, devices)
    {
        private static readonly Vector3[] Corners =
        [
            new(-0.5f, -0.5f, 0f),
            new(0.5f, -0.5f, 0f),
            new(-0.5f, 0.5f, 0f),
            new(0.5f, 0.5f, 0f),
        ];

        private readonly Flat3DEffect effect = effect;
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines = new(
                device => new RenderPipeline<TransformConstants>(
                    device,
                    new PlaneMesh(device),
                    new TextureMaterial(device)));

        // グループ制御に付けたときは、中のアイテムが持つ 3D の中身をそのまま使い、
        // 回転はグループ制御の中心を軸にして親として掛ける。
        private bool onGroup;
        private I3DProvider? groupSource;
        private VideoEffect3DProcessorBase? groupSourceProcessor;

        public override bool ScalesToInputSize => false;

        protected override void OnUpdating(EffectDescription effectDescription)
        {
            onGroup = false;
            groupSource = null;
            groupSourceProcessor = null;

            if (SceneDepthCollector.FindOwner(effectDescription) is not { } owner
                || !GroupEffectSource.IsOnGroup(owner, effect))
            {
                return;
            }

            onGroup = true;
            groupSource = GroupEffectSource.Find(owner, effectDescription.InputIndex, Devices, this);
            groupSourceProcessor = groupSource as VideoEffect3DProcessorBase;
        }

        protected override bool TryInheritTransform(out Matrix4x4 local, out Matrix4x4 world)
        {
            if (!onGroup || EffectDescription is not { } description)
                return base.TryInheritTransform(out local, out world);

            var pivot = GetRotation(FrameContext.FromItem(description));

            if (groupSourceProcessor is { } source
                && source.TryGetLocalMatrix(out var sourceLocal)
                && source.TryGetPlacement(out var sourcePlacement))
            {
                local = sourceLocal;
                world = sourcePlacement * pivot;
                return true;
            }

            local = Matrix4x4.Identity;
            world = ItemPlacement.GetWorldMatrix(description.DrawDescription) * pivot;
            return true;
        }

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (onGroup && groupSource is { } source)
            {
                source.Draw(render, new DrawContext3D
                {
                    World = item.World,
                    Opacity = item.Opacity,
                    Blend = item.Blend,
                    IsAlwaysOnTop = item.IsAlwaysOnTop,
                    DepthOnly = item.DepthOnly,
                    Time = item.Time,
                });

                return;
            }

            var texture = item.Texture ?? GetTexture(render.Device);
            if (texture is null || !pipelines.TryGet(render.Device, out var pipeline))
                return;

            var time = EffectDescription is { } description
                ? FrameContext.FromItem(description)
                : item.Time;

            var world = GetLocalMatrix(time) * item.World;

            var constants = render.CreateConstants(world, item.Opacity, effect.IsUnlit, item.AlphaCutoff, effect.GetGloss(time));

            var settings = item.ToDrawSettings(FaceCulling.None, texture) with
            {
                SkipDepthWrite = !effect.WritesDepth,
            };

            pipeline.Draw(render.Context, constants, settings);
        }

        private Matrix4x4 GetRotation(in FrameContext time)
            => Rotation3D.ForObject(
                -effect.RotationX.GetFloat(time),
                -effect.RotationY.GetFloat(time),
                -effect.RotationZ.GetFloat(time));

        private Matrix4x4 GetLocalMatrix(in FrameContext time)
        {
            // グループ制御に付けたときは、回転を親として別に掛けるので、ここでは板の大きさだけ。
            var rotation = onGroup ? Matrix4x4.Identity : GetRotation(time);

            if (!TryGetSize(out var size, out var offset))
                return rotation;

            return WorldScale.CreateSizeMatrix(size, offset + size / 2f, rotation);
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            if (onGroup && groupSource is I3DBounds bounds)
                return bounds.GetLocalBounds(itemTime);

            return WorldBounds.FromPoints(Corners, GetLocalMatrix(itemTime));
        }

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
