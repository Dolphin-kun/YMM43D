using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vortice.Direct2D1;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Player
{
    public static class GroupEffectProbe
    {
        private const long VerdictLifetimeMs = 250;

        private const long ProcessorLifetimeMs = 30_000;

        private const float Tolerance = 1e-3f;

        private static readonly Lock gate = new();

        private static readonly ConditionalWeakTable<GroupItem, Verdict> verdicts = [];

        private static readonly Dictionary<(IVideoEffect Effect, nint Device), Entry> processors = [];

        private static readonly Dictionary<nint, ID2D1CommandList> blanks = [];

        private static readonly PrivateD2DContext privateContext = new();

        private sealed record Verdict(int Frame, long At, int Signature, bool Moves);

        private sealed class Entry(IVideoEffectProcessor processor)
        {
            public IVideoEffectProcessor Processor { get; } = processor;

            public long UsedAt { get; set; } = Environment.TickCount64;
        }

        public static IReadOnlyList<IVideoEffect> EnabledEffects(GroupItem group)
            => [.. (group.VideoEffects ?? []).Where(effect => effect.IsEnabled)];

        public static DrawDescription Identity => new(
            Draw: Vector3.Zero,
            CenterPoint: Vector2.Zero,
            Zoom: Vector2.One,
            Rotation: Vector3.Zero,
            Camera: Matrix4x4.Identity,
            ZoomInterpolationMode: InterpolationMode.Linear,
            Opacity: 1d,
            Invert: false,
            Controllers: ImmutableList<VideoEffectController>.Empty);

        public static bool Moves(in DrawDescription draw)
            => draw.Draw.Length() > Tolerance
            || draw.CenterPoint.Length() > Tolerance
            || MathF.Abs(draw.Zoom.X - 1f) > Tolerance
            || MathF.Abs(draw.Zoom.Y - 1f) > Tolerance
            || draw.Rotation.Length() > Tolerance
            || !IsNearlyIdentity(draw.Camera);

        public static bool MovesPlacement(
            GroupItem group,
            IGraphicsDevicesAndContext devices,
            TimelineSourceDescription source,
            int timelineFrame)
        {
            var effects = EnabledEffects(group);

            if (effects.Count == 0)
                return false;

            var signature = Signature(effects);
            var now = Environment.TickCount64;

            if (verdicts.TryGetValue(group, out var known)
                && known.Frame == timelineFrame
                && known.Signature == signature
                && now - known.At < VerdictLifetimeMs)
            {
                return known.Moves;
            }

            var moves = Evaluate(group, effects, devices, source, timelineFrame);

            verdicts.AddOrUpdate(group, new Verdict(timelineFrame, now, signature, moves));

            return moves;
        }

        public static void Forget()
        {
            lock (gate)
            {
                foreach (var entry in processors.Values)
                    Release(entry.Processor);

                processors.Clear();

                foreach (var blank in blanks.Values)
                    blank.Dispose();

                blanks.Clear();
                verdicts.Clear();
            }
        }

        private static bool Evaluate(
            GroupItem group,
            IReadOnlyList<IVideoEffect> effects,
            IGraphicsDevicesAndContext devices,
            TimelineSourceDescription source,
            int timelineFrame)
        {
            lock (gate)
            {
                try
                {
                    var description = new TimelineItemSourceDescription(
                        source, timelineFrame - group.Frame, Math.Max(1, group.Length), group.Layer);

                    ID2D1Image image = Blank(devices);
                    var draw = Identity;

                    foreach (var effect in effects)
                    {
                        var processor = ProcessorFor(effect, devices);

                        processor.SetInput(image);
                        draw = processor.Update(new EffectDescription(description, draw, 0, 1, 0, 1));
                        image = processor.Output;
                    }

                    return Moves(draw);
                }
                catch (Exception error)
                {
                    Trace.TraceWarning($"[YMM43D] グループ制御のエフェクトを確かめられませんでした。板として扱います。{error.Message}");
                    return true;
                }
                finally
                {
                    Prune();
                }
            }
        }

        private static IVideoEffectProcessor ProcessorFor(IVideoEffect effect, IGraphicsDevicesAndContext devices)
        {
            var key = (effect, devices.D3D.Device.NativePointer);

            if (processors.TryGetValue(key, out var entry))
            {
                entry.UsedAt = Environment.TickCount64;
                return entry.Processor;
            }

            IVideoEffectProcessor processor;

            using (Provider3DRegistry.SuppressRegistration())
                processor = effect.CreateVideoEffect(devices);

            processors[key] = new Entry(processor);
            return processor;
        }

        private static ID2D1CommandList Blank(IGraphicsDevicesAndContext devices)
        {
            var key = devices.D2D.Device.NativePointer;

            if (blanks.TryGetValue(key, out var blank))
                return blank;

            var context = privateContext.For(devices);

            blank = context.CreateCommandList();
            context.Target = blank;
            context.BeginDraw();
            context.Clear(null);
            context.EndDraw();
            context.Target = null;
            blank.Close();

            return blanks[key] = blank;
        }

        private static void Prune()
        {
            var now = Environment.TickCount64;

            foreach (var key in processors.Where(pair => now - pair.Value.UsedAt > ProcessorLifetimeMs).Select(pair => pair.Key).ToArray())
            {
                Release(processors[key].Processor);
                processors.Remove(key);
            }
        }

        private static void Release(IVideoEffectProcessor processor)
        {
            try
            {
                processor.ClearInput();
                processor.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private static int Signature(IReadOnlyList<IVideoEffect> effects)
        {
            var hash = new HashCode();

            foreach (var effect in effects)
                hash.Add(RuntimeHelpers.GetHashCode(effect));

            return hash.ToHashCode();
        }

        private static bool IsNearlyIdentity(in Matrix4x4 matrix)
        {
            var identity = Matrix4x4.Identity;

            return MathF.Abs(matrix.M11 - identity.M11) < Tolerance && MathF.Abs(matrix.M12) < Tolerance
                && MathF.Abs(matrix.M13) < Tolerance && MathF.Abs(matrix.M14) < Tolerance
                && MathF.Abs(matrix.M21) < Tolerance && MathF.Abs(matrix.M22 - 1f) < Tolerance
                && MathF.Abs(matrix.M23) < Tolerance && MathF.Abs(matrix.M24) < Tolerance
                && MathF.Abs(matrix.M31) < Tolerance && MathF.Abs(matrix.M32) < Tolerance
                && MathF.Abs(matrix.M33 - 1f) < Tolerance && MathF.Abs(matrix.M34) < Tolerance
                && MathF.Abs(matrix.M41) < Tolerance && MathF.Abs(matrix.M42) < Tolerance
                && MathF.Abs(matrix.M43) < Tolerance && MathF.Abs(matrix.M44 - 1f) < Tolerance;
        }
    }
}
