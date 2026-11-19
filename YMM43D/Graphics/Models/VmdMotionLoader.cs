using System.IO;
using System.Numerics;
using System.Text;

namespace YMM43D.Graphics.Models
{
    public static class VmdMotionLoader
    {
        public const float Fps = 30f;
        private const int BoneRecord = 111;
        private const int MorphRecord = 23;

        public static ModelData Load(string path)
        {
            var full = Path.GetFullPath(path);

            return Read(File.ReadAllBytes(full), Path.GetFileNameWithoutExtension(full));
        }

        private static ModelData Read(byte[] data, string title)
        {
            if (data.Length < 54 || !Encoding.ASCII.GetString(data, 0, 25).StartsWith("Vocaloid Motion Data"))
                throw new InvalidDataException("vmd のしるしがありません。");

            var builder = new ModelBuilder();
            var shift = Shift();

            var at = 50;
            var bones = BitConverter.ToInt32(data, at);

            at += 4;

            var turns = new Dictionary<string, List<(int Frame, Vector3 Shift, Quaternion Turn)>>(StringComparer.Ordinal);

            for (var i = 0; i < bones && at + BoneRecord <= data.Length; i++, at += BoneRecord)
            {
                var name = Label(data, at, 15, shift);

                if (name.Length == 0)
                    continue;

                var frame = BitConverter.ToInt32(data, at + 15);

                var place = new Vector3(
                    BitConverter.ToSingle(data, at + 19),
                    BitConverter.ToSingle(data, at + 23),
                    -BitConverter.ToSingle(data, at + 27));

                var turn = new Quaternion(
                    -BitConverter.ToSingle(data, at + 31),
                    -BitConverter.ToSingle(data, at + 35),
                    BitConverter.ToSingle(data, at + 39),
                    BitConverter.ToSingle(data, at + 43));

                (turns.TryGetValue(name, out var found) ? found : turns[name] = [])
                    .Add((frame, place, Quaternion.Normalize(turn)));
            }

            var weights = new Dictionary<string, List<(int Frame, float Weight)>>(StringComparer.Ordinal);

            if (at + 4 <= data.Length)
            {
                var morphs = BitConverter.ToInt32(data, at);

                at += 4;

                for (var i = 0; i < morphs && at + MorphRecord <= data.Length; i++, at += MorphRecord)
                {
                    var name = Label(data, at, 15, shift);

                    if (name.Length == 0)
                        continue;

                    (weights.TryGetValue(name, out var found) ? found : weights[name] = [])
                        .Add((BitConverter.ToInt32(data, at + 15), BitConverter.ToSingle(data, at + 19)));
                }
            }

            var tracks = new List<ModelTrack>();
            var moved = new List<ModelAnimatedBone>();
            var last = 0f;

            foreach (var (name, keys) in turns)
            {
                keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));

                var bone = builder.Bones.Count;

                builder.Bones.Add(new ModelBone(name, -1, Matrix4x4.Identity, Matrix4x4.Identity));
                moved.Add(new ModelAnimatedBone(bone, Vector3.Zero, Quaternion.Identity, Vector3.One, Matrix4x4.Identity));

                var times = new float[keys.Count];
                var places = new float[keys.Count * 3];
                var spins = new float[keys.Count * 4];
                var moves = false;
                var spun = false;

                for (var i = 0; i < keys.Count; i++)
                {
                    var (frame, place, turn) = keys[i];

                    times[i] = frame / Fps;
                    places[i * 3] = place.X;
                    places[i * 3 + 1] = place.Y;
                    places[i * 3 + 2] = place.Z;
                    spins[i * 4] = turn.X;
                    spins[i * 4 + 1] = turn.Y;
                    spins[i * 4 + 2] = turn.Z;
                    spins[i * 4 + 3] = turn.W;

                    moves |= place != Vector3.Zero;
                    spun |= turn != Quaternion.Identity;
                }

                last = MathF.Max(last, times[^1]);

                if (spun)
                    tracks.Add(new ModelTrack(bone, -1, ModelTrackKind.Rotation, times, spins, false));

                if (moves)
                    tracks.Add(new ModelTrack(bone, -1, ModelTrackKind.Translation, times, places, false));
            }

            foreach (var (name, keys) in weights)
            {
                keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));

                var morph = builder.Morphs.Count;

                builder.Morphs.Add(new ModelMorph(name, []));

                var times = new float[keys.Count];
                var values = new float[keys.Count];

                for (var i = 0; i < keys.Count; i++)
                {
                    times[i] = keys[i].Frame / Fps;
                    values[i] = keys[i].Weight;
                }

                last = MathF.Max(last, times[^1]);

                tracks.Add(new ModelTrack(-1, morph, ModelTrackKind.Weight, times, values, false));
            }

            if (tracks.Count == 0)
                throw new InvalidDataException("vmd に動きが入っていません。");

            builder.Animations.Add(new ModelAnimation(
                title.Length > 0 ? title : "動き", last, [.. tracks], [.. moved])
            {
                Relative = true,
            });

            return builder.Build();
        }

        private static string Label(byte[] data, int at, int length, Encoding shift)
        {
            var end = 0;

            while (end < length && data[at + end] != 0)
                end++;

            return end == 0 ? string.Empty : shift.GetString(data, at, end).Trim();
        }

        private static Encoding Shift()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding(932);
        }
    }
}
