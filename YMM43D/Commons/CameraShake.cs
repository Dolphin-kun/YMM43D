using System.Numerics;

namespace YMM43D.Commons
{
    public static class CameraShake
    {
        public const int LeastDetail = 1;
        public const int MostDetail = 4;

        public static Vector3 Wobble(float at, int detail, int seed)
        {
            if (!float.IsFinite(at))
                return Vector3.Zero;

            var steps = Math.Clamp(detail, LeastDetail, MostDetail);
            var gathered = Vector3.Zero;
            var total = 0f;
            var amount = 1f;
            var speed = 1f;

            for (var step = 0; step < steps; step++)
            {
                var channel = seed * 7 + step * 3;

                gathered += new Vector3(Wave(at * speed, channel), Wave(at * speed, channel + 1), Wave(at * speed, channel + 2)) * amount;

                total += amount;
                amount *= 0.5f;
                speed *= 2.03f;
            }

            return total > 0f ? gathered / total : Vector3.Zero;
        }

        private static float Wave(float at, int channel)
        {
            var cell = (int)MathF.Floor(at);
            var gap = at - cell;
            var eased = gap * gap * (3f - 2f * gap);

            return float.Lerp(Spot(cell, channel), Spot(cell + 1, channel), eased) * 2f - 1f;
        }

        private static float Spot(int cell, int channel)
        {
            unchecked
            {
                var mixed = (uint)(cell * 374761393 + channel * 668265263 + 1013904223);

                mixed = (mixed ^ (mixed >> 13)) * 1274126177u;
                mixed ^= mixed >> 16;

                return (mixed & 0xFFFFFFu) / (float)0xFFFFFF;
            }
        }
    }
}
