using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Commons
{
    public readonly record struct FrameContext(int Frame, int Length, int Fps)
    {
        public static FrameContext FromItem(TimelineItemSourceDescription description) => new(
            description.ItemPosition.Frame,
            Math.Max(1, description.ItemDuration.Frame),
            Math.Max(1, description.FPS));

        public static FrameContext FromTimeline(TimelineItemSourceDescription description)
        {
            if (description.TimelineDuration.Frame <= 0 || description.FPS <= 0)
                return FromItem(description);

            return new FrameContext(
                description.TimelinePosition.Frame,
                description.TimelineDuration.Frame,
                description.FPS);
        }
    }

    public static class AnimationExtensions
    {
        public static double GetValue(this Animation animation, in FrameContext context)
            => animation.GetValue(context.Frame, context.Length, context.Fps);

        public static float GetFloat(this Animation animation, in FrameContext context)
            => (float)animation.GetValue(context.Frame, context.Length, context.Fps);

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

            var target = values[index];

            target.Value = Math.Clamp(target.Value + delta, animation.MinValue, animation.MaxValue);
        }
    }
}
