using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Player;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;

namespace FrameImpact3D
{
    internal sealed class FrameImpact3DProcessor(FrameImpact3DEffect effect, IGraphicsDevicesAndContext devices) : VideoEffect3DProcessorBase(effect, devices)
    {
        private static readonly WorldBounds UnitPlane = new(new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f));

        private readonly DeviceResourceCache<ImpactResources> resources = new(device => new ImpactResources(device));

        private Matrix4x4? groupPlacement;
        private bool leadsGroup = true;

        protected override bool KeepsGroupMemberSolid => true;

        protected override bool UsesGroupMemberBounds => false;

        protected override bool AcceptsOwnSolid(I3DProvider solid, in FrameContext itemTime) => true;

        protected override void OnUpdating(EffectDescription effectDescription)
        {
            groupPlacement = null;
            leadsGroup = true;

            if (!IsGroupEffect || TimelineLookup.Find(effectDescription) is not { } timeline)
                return;

            var frame = effectDescription.TimelinePosition.Frame;
            var holder = timeline.Items.OfType<GroupItem>().FirstOrDefault(group => FrameContext.IsAlive(group, frame) && Holds(group));

            if (holder is null)
                return;

            var first = int.MaxValue;

            foreach (var item in timeline.Items)
            {
                if (item.Layer > holder.Layer
                    && item.Layer <= holder.Layer + holder.GroupRange
                    && item.Layer < first
                    && IsDrawnMember(item)
                    && FrameContext.IsAlive(item, frame)
                    && LayerVisibility.IsShown(timeline, item))
                {
                    first = item.Layer;
                }
            }

            groupPlacement = ItemPlacement.GetWorldMatrix(holder, FrameContext.ForItem(holder, frame, effectDescription.FPS));
            leadsGroup = effectDescription.Layer == first;
        }

        private bool Holds(GroupItem group) => group.VideoEffects?.Any(candidate => ReferenceEquals(candidate, effect)) == true;

        private static bool IsDrawnMember(IItem item) => item is IVideoItem and not (GroupItem or ISceneCamera or ISceneLightSource or ISceneEnvironment);

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (!resources.TryGet(render.Device, out var shared))
                return;

            var time = EffectDescription is { } description ? FrameContext.FromItem(description) : item.Time;
            var moment = effect.FindMoment(time);

            if (moment is not { } found)
            {
                DrawBase(render, item, shared);
                return;
            }

            var camera = ImpactCamera.From(render.View);
            var anchor = ImpactAnchor.From(Anchor(item), effect.GetCenter(time));
            var aim = ImpactAim.From(effect, time, anchor, camera);
            var shaken = found.IsStriking ? Shaken(item, ShakeOffset(time, found, anchor, aim, camera)) : item;

            DrawStyled(render, shaken, shared, effect.GetFrameLook(found, time, aim), anchor.Center);

            if (render.IsShadowPass || item.DepthOnly || !leadsGroup || !found.IsStriking)
                return;

            shared.Impact.Draw(render, effect, time, found, anchor, aim, item.Opacity, item.IsAlwaysOnTop);
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            var own = GroupMemberSolid switch
            {
                I3DBounds bounded => bounded.GetLocalBounds(itemTime),
                null => UnitPlane,
                _ => WorldBounds.Empty,
            };

            if (effect.FindMoment(itemTime) is not { } moment
                || !TryGetLocalMatrix(out var local)
                || !TryGetPlacement(out var placement)
                || !Matrix4x4.Invert(local * placement, out var undo))
            {
                return own;
            }

            var world = own.Transform(local * placement);
            var scale = ImpactAnchor.ScaleOf(placement);
            var spread = new Vector3((effect.GetShake(itemTime) + effect.GetFrameSpread(itemTime)) * scale);
            var spanned = new WorldBounds(world.Min - spread, world.Max + spread);

            if (moment.IsStriking && leadsGroup)
            {
                var anchor = groupPlacement ?? placement;
                var center = Vector3.Transform(effect.GetCenter(itemTime), anchor);
                var reach = new Vector3(effect.GetReach(itemTime) * ImpactAnchor.ScaleOf(anchor));

                spanned = new WorldBounds(Vector3.Min(spanned.Min, center - reach), Vector3.Max(spanned.Max, center + reach));
            }

            return spanned.Transform(undo);
        }

        public override void Dispose()
        {
            resources.Dispose();
            base.Dispose();
        }

        private Matrix4x4 Anchor(DrawContext3D item)
        {
            if (groupPlacement is { } group)
                return group;

            return TryGetLocalMatrix(out var local) && Matrix4x4.Invert(local, out var undo) ? undo * item.World : item.World;
        }

        private Vector3 ShakeOffset(in FrameContext time, in ImpactMoment moment, in ImpactAnchor anchor, in ImpactAim aim, in ImpactCamera camera)
        {
            var amount = effect.GetShake(time) * anchor.Scale;

            if (amount <= 0f)
                return Vector3.Zero;

            var rest = 1f - moment.Progress;
            var step = (int)MathF.Floor(moment.Seconds * effect.ShakeSpeed.GetFloat(time));
            var focus = aim.Focus;
            var loose = (camera.Right * Jitter(moment.Seed, step, 0) + camera.Up * Jitter(moment.Seed, step, 1)) * (1f - focus * 0.7f);
            var pushed = aim.Direction * Jitter(moment.Seed, step, 2) * focus;

            return (loose + pushed) * amount * rest * rest;
        }

        private static float Jitter(int seed, int step, int axis)
        {
            var hash = (uint)seed * 0x9E3779B1u ^ (uint)step * 0x85EBCA77u ^ (uint)axis * 0xC2B2AE3Du;

            hash ^= hash >> 15;
            hash *= 0x2C1B3C6Du;
            hash ^= hash >> 12;
            hash *= 0x297A2D39u;
            hash ^= hash >> 15;

            return hash / (float)uint.MaxValue * 2f - 1f;
        }

        private static DrawContext3D Shaken(DrawContext3D item, Vector3 offset) => offset == Vector3.Zero ? item : new DrawContext3D
        {
            World = item.World * Matrix4x4.CreateTranslation(offset),
            Opacity = item.Opacity,
            Blend = item.Blend,
            IsAlwaysOnTop = item.IsAlwaysOnTop,
            DepthOnly = item.DepthOnly,
            Time = item.Time,
            Texture = item.Texture,
        };

        private void DrawStyled(in Render3DContext render, DrawContext3D item, ImpactResources shared, ImpactFrameLook? look, Vector3 center)
        {
            if (look is not { } frame
                || render.IsShadowPass
                || render.IsCapturingScene
                || item.DepthOnly
                || shared.Frame.Begin(render.Context) is not { } target)
            {
                DrawBase(render, item, shared);
                return;
            }

            try
            {
                DrawBase(render, item, shared);
            }
            finally
            {
                shared.Frame.End(render, target, frame, center, item.IsAlwaysOnTop);
            }
        }

        private void DrawBase(in Render3DContext render, DrawContext3D item, ImpactResources shared)
        {
            if (TryDrawGroupMemberSolid(render, item))
                return;

            if ((item.Texture ?? GetTexture(render.Device)) is not { } texture)
                return;

            var constants = render.CreateConstants(item.World, item.Opacity, alphaCutoff: item.AlphaCutoff);

            shared.Plane.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.None, texture));
        }

        private sealed class ImpactResources(ID3D11Device device) : IDisposable
        {
            public RenderPipeline<TransformConstants> Plane { get; } = new(device, new PlaneMesh(device), new TextureMaterial(device));

            public ImpactRenderer Impact { get; } = new(device);

            public ImpactFramePass Frame { get; } = new(device);

            public void Dispose()
            {
                Frame.Dispose();
                Impact.Dispose();
                Plane.Dispose();
            }
        }
    }
}
