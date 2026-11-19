using System.Numerics;

namespace Bezier3D
{
    public readonly record struct PathPoint(Vector3 Position, float At);

    public static class BezierPath
    {
        public const int LeastSegments = 1;
        public const int MostSegments = 64;

        public static readonly float[] Dash = [2f, 2f];
        public static readonly float[] Dot = [0.2f, 2f];
        public static readonly float[] DashDot = [2f, 2f, 0.2f, 2f];
        public static readonly float[] DashDotDot = [2f, 2f, 0.2f, 2f, 0.2f, 2f];

        public static float[] Parse(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return [];

            var found = new List<float>();

            foreach (var part in pattern.Split([',', ' ', '、'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (float.TryParse(part, out var value) && float.IsFinite(value) && value >= 0f)
                    found.Add(value);
            }

            if (found.Count == 0)
                return [];

            if (found.Count % 2 == 1)
                found.Add(found[^1]);

            return found.Sum() > 0f ? [.. found] : [];
        }

        public static Vector3[] Trace(IReadOnlyList<Vector3> points, BezierLineKind kind, bool closed, int perSegment)
        {
            if (points.Count == 0)
                return [];

            if (points.Count == 1)
                return [points[0]];

            var steps = Math.Clamp(perSegment, LeastSegments, MostSegments);
            var walked = new List<Vector3> { points[0] };

            void Curve(Vector3 from, Vector3 first, Vector3 second, Vector3 to)
            {
                for (var step = 1; step <= steps; step++)
                {
                    var at = (float)step / steps;
                    var rest = 1f - at;

                    walked.Add(from * (rest * rest * rest) + first * (3f * rest * rest * at) + second * (3f * rest * at * at) + to * (at * at * at));
                }
            }

            void Quad(Vector3 from, Vector3 middle, Vector3 to) => Curve(from, from + (middle - from) * (2f / 3f), to + (middle - to) * (2f / 3f), to);

            var anchor = points[0];
            var index = 1;

            while (index < points.Count)
            {
                if (kind == BezierLineKind.Cubic && index + 2 < points.Count)
                {
                    Curve(anchor, points[index], points[index + 1], points[index + 2]);
                    anchor = points[index + 2];
                    index += 3;
                    continue;
                }

                if (kind != BezierLineKind.Straight && index + 1 < points.Count)
                {
                    Quad(anchor, points[index], points[index + 1]);
                    anchor = points[index + 1];
                    index += 2;
                    continue;
                }

                walked.Add(points[index]);
                anchor = points[index];
                index++;
            }

            if (closed && (anchor - points[0]).LengthSquared() > 1e-12f)
                walked.Add(points[0]);

            return [.. Packed(walked)];
        }

        public static List<PathPoint[]> Runs(IReadOnlyList<Vector3> path, float[] pattern, float unit, float offset, float lengthRate)
        {
            var runs = new List<PathPoint[]>();

            if (path.Count < 2)
                return runs;

            var lengths = Lengths(path);
            var total = lengths[^1];

            if (total <= 0f)
                return runs;

            var keep = total * Math.Clamp(lengthRate, 0f, 1f);

            if (keep <= 0f)
                return runs;

            if (pattern.Length == 0 || unit <= 0f)
            {
                runs.Add(Slice(path, lengths, 0f, keep, total));
                return runs;
            }

            var cycle = pattern.Sum() * unit;
            var start = cycle > 0f ? -(offset * unit % cycle) : 0f;

            var walked = start;
            var index = 0;

            while (walked < keep && runs.Count < 4096)
            {
                var span = MathF.Max(pattern[index % pattern.Length] * unit, 1e-4f);
                var on = index % 2 == 0;
                var from = walked;
                var to = walked + span;

                if (on && to > 0f)
                {
                    var cut = Slice(path, lengths, MathF.Max(from, 0f), MathF.Min(to, keep), total);

                    if (cut.Length >= 2)
                        runs.Add(cut);
                }

                walked = to;
                index++;
            }

            return runs;
        }

        private static PathPoint[] Slice(IReadOnlyList<Vector3> path, float[] lengths, float from, float to, float total)
        {
            if (to <= from)
                return [];

            var made = new List<PathPoint> { new(Along(path, lengths, from), from / total) };

            for (var i = 0; i < path.Count; i++)
            {
                if (lengths[i] <= from || lengths[i] >= to)
                    continue;

                made.Add(new PathPoint(path[i], lengths[i] / total));
            }

            made.Add(new PathPoint(Along(path, lengths, to), to / total));

            return [.. made];
        }

        private static Vector3 Along(IReadOnlyList<Vector3> path, float[] lengths, float at)
        {
            if (at <= 0f)
                return path[0];

            if (at >= lengths[^1])
                return path[^1];

            for (var i = 1; i < path.Count; i++)
            {
                if (lengths[i] < at)
                    continue;

                var span = lengths[i] - lengths[i - 1];
                var share = span > 1e-9f ? (at - lengths[i - 1]) / span : 0f;

                return Vector3.Lerp(path[i - 1], path[i], share);
            }

            return path[^1];
        }

        private static float[] Lengths(IReadOnlyList<Vector3> path)
        {
            var made = new float[path.Count];

            for (var i = 1; i < path.Count; i++)
                made[i] = made[i - 1] + Vector3.Distance(path[i - 1], path[i]);

            return made;
        }

        private static List<Vector3> Packed(List<Vector3> walked)
        {
            var made = new List<Vector3> { walked[0] };

            foreach (var point in walked)
            {
                if (Vector3.DistanceSquared(made[^1], point) > 1e-12f)
                    made.Add(point);
            }

            return made;
        }
    }
}
