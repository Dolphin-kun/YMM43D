using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public static class AnimationExtensions
    {
        public const double MinSegmentWeight = 0.2;

        public static double GetValue(this Animation animation, in FrameContext context) => animation.GetValue(context.Frame, context.Length, context.Fps);

        public static float GetFloat(this Animation animation, in FrameContext context)
            => (float)animation.GetValue(context.Frame, context.Length, context.Fps);

        public static int SegmentOf(this Animation animation, int frame)
        {
            var index = 0;

            if (animation.KeyFrames?.Frames is { } frames)
            {
                for (var i = 0; i < frames.Count; i++)
                {
                    if (frame >= frames[i])
                        index = i + 1;
                }
            }

            return index;
        }

        public static double WeightOf(this Animation animation, int index, in FrameContext time)
        {
            if (animation.Values is not { } values || index < 0 || index >= values.Count)
                return 0;

            var value = values[index].Value;
            var up = Math.Min(1d, animation.MaxValue - value);
            var probe = up > 1e-9 ? up : -Math.Min(1d, value - animation.MinValue);

            if (Math.Abs(probe) < 1e-9)
                return 0;

            var before = animation.GetValue(time);

            MoveValue(animation, index, probe);

            var moved = animation.GetValue(time) - before;

            MoveValue(animation, index, -probe);

            return moved / probe;
        }

        public static void NudgeSegment(this Animation animation, double delta, in FrameContext time)
        {
            if (delta == 0)
                return;

            if (animation.Values is not { Count: > 1 })
            {
                animation.Nudge(delta);
                return;
            }

            var values = animation.Values;

            var start = animation.SegmentOf(time.Frame);
            var chosen = start;
            var weight = animation.WeightOf(start, time);

            if (start + 1 < values.Count && animation.WeightOf(start + 1, time) is var next && next >= MinSegmentWeight)
            {
                chosen = start + 1;
                weight = next;
            }

            if (weight < 1e-3)
            {
                animation.Nudge(delta);
                return;
            }

            MoveValue(animation, chosen, delta / Math.Max(weight, MinSegmentWeight));
        }

        public static void Nudge(this Animation animation, double delta)
        {
            if (delta == 0 || animation.Values is not { Count: > 0 } values)
                return;

            var room = Math.Min(animation.MaxValue - values.Max(v => v.Value), delta);
            room = Math.Max(animation.MinValue - values.Min(v => v.Value), room);

            if (room == 0 || Math.Sign(room) != Math.Sign(delta))
                return;

            animation.AddToEachValues(room);
        }

        public static void NudgeAt(this Animation animation, double delta, int frame)
        {
            if (delta == 0)
                return;

            if (frame <= 0 || animation.KeyFrames is not { } keyFrames)
            {
                NudgeValue(animation, delta, 0);
                return;
            }

            if (!AnimationTypeEx.IsKeyFrameSupported(animation.AnimationType))
                animation.AnimationType = AnimationType.直線移動;

            var index = keyFrames.Frames.IndexOf(frame);
            if (index < 0)
                index = keyFrames.Insert(frame);

            NudgeValue(animation, delta, index + 1);
        }

        private static void NudgeValue(Animation animation, double delta, int index)
        {
            if (animation.Values is not { } values || index < 0 || index >= values.Count)
            {
                animation.Nudge(delta);
                return;
            }

            MoveValue(animation, index, delta);
        }

        private static void MoveValue(Animation animation, int index, double delta)
        {
            if (animation.Values is not { } values || index < 0 || index >= values.Count)
                return;

            var target = values[index];

            target.Value = Math.Clamp(target.Value + delta, animation.MinValue, animation.MaxValue);
        }
    }
}
