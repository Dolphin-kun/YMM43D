using YukkuriMovieMaker.Plugin.Voice;

namespace YMM43D.Project.Tachie
{
    internal sealed class MouthMotion
    {
        public const int Slots = 6;

        private const float CloseRatio = 2f;

        private const int MaxStep = 4;

        private const double LeastOpen = 0.5;

        private readonly float[] weights = new float[Slots];

        private int lastFrame = int.MinValue;

        public IReadOnlyList<float> Weights => weights;

        public void Advance(MouthShape shape, double voice, double open, double smooth, int frame, int fps)
        {
            var step = frame - lastFrame;
            var seconds = fps > 0 && step > 0 && step <= MaxStep ? step / (float)fps : -1f;

            lastFrame = frame;

            var slot = (int)(shape is MouthShape.Silent ? MouthShape.A : shape);
            var target = Target(shape, voice, open);

            for (var i = 0; i < weights.Length; i++)
            {
                var wanted = i == slot ? target : 0f;
                var ease = Ease(seconds, (float)smooth, wanted > weights[i]);

                weights[i] += (wanted - weights[i]) * ease;

                if (weights[i] < 0.002f)
                    weights[i] = 0f;
            }
        }

        private static float Ease(float seconds, float smooth, bool opening)
        {
            var tau = smooth * (opening ? 1f : CloseRatio);

            return seconds > 0f && tau > 0f ? 1f - MathF.Exp(-seconds / tau) : 1f;
        }

        private static float Target(MouthShape shape, double voice, double open)
        {
            var loud = voice > 0.0 ? Math.Clamp(voice, 0.0, 1.0) : 0.0;

            if (shape is MouthShape.Silent)
                return (float)(loud * open);

            return (float)(Math.Max(loud, LeastOpen) * open);
        }

        public static MouthShape Of(int slot) => (MouthShape)slot;
    }
}
