using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public static class ModelAnimationSampler
    {
        public static void Sample(ModelAnimation animation, float seconds, Matrix4x4[] pose, float[]? weights)
        {
            if (animation.Bones.Length == 0 && animation.Tracks.Length == 0)
                return;

            var moved = new Dictionary<int, (Vector3 T, Quaternion R, Vector3 S, Vector3 A)>(animation.Bones.Length);

            foreach (var bone in animation.Bones)
                moved[bone.Bone] = (bone.Translation, bone.Rotation, bone.Scale, bone.Angles);

            foreach (var track in animation.Tracks)
            {
                if (track.Kind == ModelTrackKind.Weight)
                {
                    if (weights is not null && track.Morph >= 0 && track.Morph < weights.Length)
                        weights[track.Morph] = Value(track, seconds, 1)[0];

                    continue;
                }

                if (!moved.TryGetValue(track.Bone, out var at))
                    continue;

                moved[track.Bone] = track.Kind switch
                {
                    ModelTrackKind.Translation => at with { T = Vector(track, seconds) },
                    ModelTrackKind.Scale => at with { S = Vector(track, seconds) },
                    ModelTrackKind.Angles => at with { A = Vector(track, seconds) },
                    _ => at with { R = Turn(track, seconds) },
                };
            }

            foreach (var bone in animation.Bones)
            {
                if (bone.Bone < 0 || bone.Bone >= pose.Length || !moved.TryGetValue(bone.Bone, out var at))
                    continue;

                var local = bone.Joint is { } joint
                    ? joint.Compose(at.T, at.A, at.S)
                    : Matrix4x4.CreateScale(at.S)
                      * Matrix4x4.CreateFromQuaternion(at.R)
                      * Matrix4x4.CreateTranslation(at.T);

                var made = local * bone.RestInverse;

                pose[bone.Bone] = bone.Facing is { } facing ? facing * made : made;
            }
        }

        private static Vector3 Vector(ModelTrack track, float seconds)
        {
            var value = Value(track, seconds, 3);

            return new Vector3(value[0], value[1], value[2]);
        }

        private static Quaternion Turn(ModelTrack track, float seconds)
        {
            var (first, second, blend) = Place(track, seconds, 4);

            var a = Read(track.Values, first);
            var b = Read(track.Values, second);

            return blend <= 0f ? a : blend >= 1f ? b : Quaternion.Normalize(Quaternion.Slerp(a, b, blend));
        }

        private static Quaternion Read(float[] values, int at) => new(values[at], values[at + 1], values[at + 2], values[at + 3]);

        private static float[] Value(ModelTrack track, float seconds, int width)
        {
            var (first, second, blend) = Place(track, seconds, width);
            var made = new float[width];

            for (var i = 0; i < width; i++)
            {
                var a = track.Values[first + i];
                var b = track.Values[second + i];

                made[i] = a + (b - a) * blend;
            }

            return made;
        }

        private static (int First, int Second, float Blend) Place(ModelTrack track, float seconds, int width)
        {
            var times = track.Times;
            var last = times.Length - 1;

            if (seconds <= times[0])
                return (0, 0, 0f);

            if (seconds >= times[last])
                return (last * width, last * width, 0f);

            var low = 0;
            var high = last;

            while (high - low > 1)
            {
                var middle = (low + high) / 2;

                if (times[middle] <= seconds)
                    low = middle;
                else
                    high = middle;
            }

            if (track.Stepped)
                return (low * width, low * width, 0f);

            var span = times[high] - times[low];
            var blend = span > 1e-9f ? (seconds - times[low]) / span : 0f;

            return (low * width, high * width, blend);
        }
    }
}
