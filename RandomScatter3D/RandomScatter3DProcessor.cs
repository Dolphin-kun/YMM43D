using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;
using YMM43D.Player;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace RandomScatter3D
{
    internal sealed class RandomScatter3DProcessor(RandomScatter3DEffect effect, IGraphicsDevicesAndContext devices)
        : VideoEffect3DProcessorBase(effect, devices)
    {
        private readonly RandomScatter3DEffect effect = effect;
        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines = new(
            device => new RenderPipeline<TransformConstants>(device, new PlaneMesh(device), new TextureMaterial(device)));

        private I3DProvider? solidSource;

        public override bool ScalesToInputSize => false;

        protected override void OnUpdating(EffectDescription effectDescription)
            => solidSource = FindSolidSource(effectDescription);

        private I3DProvider? FindSolidSource(EffectDescription description)
            => SceneDepthCollector.FindOwner(description) is { } owner
                ? SceneDepthCollector.FindSources(owner, Devices).FirstOrDefault(source => !ReferenceEquals(source, this))
                : null;

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var time = EffectDescription is { } description ? FrameContext.FromItem(description) : item.Time;

            Matrix4x4.Invert(render.View, out var cameraWorld);
            var facing = cameraWorld with { M41 = 0f, M42 = 0f, M43 = 0f, M44 = 1f };

            if (solidSource is { } source)
            {
                foreach (var copy in Copies(time))
                {
                    source.Draw(render, new DrawContext3D
                    {
                        World = Place(copy, Matrix4x4.CreateScale(copy.Scale), facing, item.World),
                        Opacity = item.Opacity,
                        Blend = item.Blend,
                        IsAlwaysOnTop = item.IsAlwaysOnTop,
                        DepthOnly = item.DepthOnly,
                        Time = item.Time,
                    });
                }

                return;
            }

            var texture = item.Texture ?? GetTexture(render.Device);

            if (texture is null || !TryGetSize(out var size, out _) || !pipelines.TryGet(render.Device, out var pipeline))
                return;

            var gloss = effect.GetGloss(time);
            var settings = item.ToDrawSettings(FaceCulling.None, texture);

            foreach (var copy in Copies(time))
            {
                var scale = Matrix4x4.CreateScale(WorldScale.ToWorld(size.X * copy.Scale), WorldScale.ToWorld(size.Y * copy.Scale), 1f);

                pipeline.Draw(
                    render.Context,
                    render.CreateConstants(Place(copy, scale, facing, item.World), item.Opacity, effect.IsUnlit, item.AlphaCutoff, gloss),
                    settings);
            }
        }

        private Matrix4x4 Place(in Copy copy, in Matrix4x4 scale, in Matrix4x4 facing, in Matrix4x4 itemWorld)
        {
            var position = WorldScale.ToWorldPosition(copy.Position.X, copy.Position.Y, copy.Position.Z);

            return effect.FacesCamera
                ? scale * Matrix4x4.CreateRotationZ(-float.DegreesToRadians(copy.Angles.Z)) * facing
                  * Matrix4x4.CreateTranslation(Vector3.Transform(position, itemWorld))
                : scale * Rotation3D.ForObject(copy.Angles.X, copy.Angles.Y, copy.Angles.Z)
                  * Matrix4x4.CreateTranslation(position) * itemWorld;
        }

        private readonly record struct Copy(Vector3 Position, Vector3 Angles, float Scale);

        private IEnumerable<Copy> Copies(FrameContext time)
        {
            var range = effect.GetRange(time);
            var maxAngle = effect.MaxAngle.GetFloat(time);
            var variation = Math.Clamp(effect.SizeVariation.GetFloat(time) / 100f, 0f, 1f);
            var maxSpeed = effect.MaxSpeed.GetFloat(time);
            var maxSpin = effect.MaxSpin.GetFloat(time);
            var frame = time.Frame;
            var seed = effect.Seed;

            for (var index = 0; index < effect.Count; index++)
            {
                var start = new Vector3(Signed(seed, index, 0), Signed(seed, index, 1), Signed(seed, index, 2)) * range / 2f;
                var velocity = Direction(seed, index) * (Unit(seed, index, 6) * maxSpeed);

                var angles = new Vector3(Signed(seed, index, 7), Signed(seed, index, 8), Signed(seed, index, 9)) * maxAngle
                           + new Vector3(Signed(seed, index, 10), Signed(seed, index, 11), Signed(seed, index, 12)) * maxSpin * frame;

                yield return new Copy(
                    Wrap(start + velocity * frame, range),
                    angles,
                    1f - variation * Unit(seed, index, 13));
            }
        }

        private static float Unit(int seed, int index, int channel)
            => Math.Clamp(StatelessRandom.GetFloat([seed, index, channel]), 0f, 1f);

        private static float Signed(int seed, int index, int channel) => Unit(seed, index, channel) * 2f - 1f;

        private static Vector3 Direction(int seed, int index)
        {
            var z = Signed(seed, index, 3);
            var angle = Unit(seed, index, 4) * MathF.Tau;
            var ring = MathF.Sqrt(MathF.Max(1f - z * z, 0f));

            return new Vector3(ring * MathF.Cos(angle), ring * MathF.Sin(angle), z);
        }

        private static Vector3 Wrap(Vector3 position, Vector3 range)
        {
            static float Axis(float value, float span)
                => span <= 0f ? 0f : ((value + span / 2f) % span + span) % span - span / 2f;

            return new Vector3(Axis(position.X, range.X), Axis(position.Y, range.Y), Axis(position.Z, range.Z));
        }

        protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
        {
            var range = effect.GetRange(itemTime);

            var reach = solidSource is I3DBounds solid && solid.GetLocalBounds(itemTime) is { IsEmpty: false } bounds
                ? MathF.Max(bounds.Min.Length(), bounds.Max.Length())
                : TryGetSize(out var size, out _) ? WorldScale.ToWorld(MathF.Max(size.X, size.Y) / 2f) : 0f;

            var extent = Vector3.Abs(WorldScale.ToWorldPosition(range.X / 2f, range.Y / 2f, range.Z / 2f)) + new Vector3(reach);

            return new WorldBounds(-extent, extent);
        }

        public override void Dispose()
        {
            pipelines.Dispose();
            base.Dispose();
        }
    }
}
