using System.Numerics;
using System.Runtime.CompilerServices;

namespace YMM43D.Graphics.Models
{
    public static class ModelMotions
    {
        private const int MostRemembered = 8;

        private static readonly ConditionalWeakTable<ModelData, Dictionary<ModelData, ModelAnimation[]>> made = [];

        public static IReadOnlyList<ModelAnimation> For(ModelData model, string? file)
        {
            if (string.IsNullOrWhiteSpace(file) || ModelLibrary.Find(file) is not { } source || source == model)
                return [];

            var remembered = made.GetOrCreateValue(model);

            lock (remembered)
            {
                if (remembered.TryGetValue(source, out var known))
                    return known;
            }

            var built = Retarget(model, source);

            lock (remembered)
            {
                if (remembered.Count >= MostRemembered)
                    remembered.Clear();

                remembered[source] = built;
            }

            return built;
        }

        public static ModelAnimation? Find(ModelData model, string? file, string name)
        {
            if (name.Length == 0)
                return null;

            foreach (var one in model.Animations)
            {
                if (one.Name == name)
                    return one;
            }

            foreach (var one in For(model, file))
            {
                if (one.Name == name)
                    return one;
            }

            return null;
        }

        public static IReadOnlyList<string> Names(ModelData model, string? file)
        {
            var names = new List<string>();

            foreach (var one in model.Animations)
            {
                if (!names.Contains(one.Name))
                    names.Add(one.Name);
            }

            foreach (var one in For(model, file))
            {
                if (!names.Contains(one.Name))
                    names.Add(one.Name);
            }

            return names;
        }

        private static float[] Lift(float[] values, in Vector3 rest, float scale)
        {
            var made = new float[values.Length];

            for (var i = 0; i + 2 < values.Length; i += 3)
            {
                made[i] = rest.X + (values[i] - rest.X) * scale;
                made[i + 1] = rest.Y + (values[i + 1] - rest.Y) * scale;
                made[i + 2] = rest.Z + (values[i + 2] - rest.Z) * scale;
            }

            return made;
        }

        private static bool Steady(in Matrix4x4 frame, out Matrix4x4 turn)
        {
            var x = new Vector3(frame.M11, frame.M12, frame.M13);
            var y = new Vector3(frame.M21, frame.M22, frame.M23);
            var z = new Vector3(frame.M31, frame.M32, frame.M33);

            turn = Matrix4x4.Identity;

            if (x.LengthSquared() < 1e-12f || y.LengthSquared() < 1e-12f || z.LengthSquared() < 1e-12f)
                return false;

            x = Vector3.Normalize(x);
            y = Vector3.Normalize(y);
            z = Vector3.Normalize(z);

            turn = new Matrix4x4(
                x.X, x.Y, x.Z, 0f,
                y.X, y.Y, y.Z, 0f,
                z.X, z.Y, z.Z, 0f,
                0f, 0f, 0f, 1f);

            return true;
        }

        private static bool Alike(in Matrix4x4 a, in Matrix4x4 b)
        {
            var slack = MathF.Max(MathF.Max(a.Translation.Length(), b.Translation.Length()) * 2e-3f, 1e-4f);

            if ((a.Translation - b.Translation).Length() > slack)
                return false;

            return MathF.Abs(a.M11 - b.M11) < 2e-3f
                && MathF.Abs(a.M12 - b.M12) < 2e-3f
                && MathF.Abs(a.M13 - b.M13) < 2e-3f
                && MathF.Abs(a.M21 - b.M21) < 2e-3f
                && MathF.Abs(a.M22 - b.M22) < 2e-3f
                && MathF.Abs(a.M23 - b.M23) < 2e-3f
                && MathF.Abs(a.M31 - b.M31) < 2e-3f
                && MathF.Abs(a.M32 - b.M32) < 2e-3f
                && MathF.Abs(a.M33 - b.M33) < 2e-3f;
        }

        private static ModelAnimation[] Retarget(ModelData model, ModelData source)
        {
            var bones = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < model.Bones.Length; i++)
                bones.TryAdd(model.Bones[i].Name, i);

            var human = new Dictionary<int, int>();

            foreach (var (name, from) in source.HumanBones)
            {
                if (model.HumanBones.TryGetValue(name, out var onto))
                    human.TryAdd(from, onto);
            }

            var morphs = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < model.Morphs.Length; i++)
                morphs.TryAdd(model.Morphs[i].Name, i);

            int? Pick(int from)
            {
                if (human.TryGetValue(from, out var found))
                    return found;

                return bones.TryGetValue(source.Bones[from].Name, out var byName) ? byName : null;
            }

            var ontoRest = ModelPose.Compute(model.Bones).Globals;
            var fromRest = ModelPose.Compute(source.Bones).Globals;

            Matrix4x4? Facing(int from, int onto)
            {
                if (!Steady(fromRest[from] * source.Root, out var was)
                    || !Steady(ontoRest[onto] * model.Root, out var now)
                    || !Matrix4x4.Invert(was, out var back))
                {
                    return null;
                }

                var turn = now * back;

                return Alike(turn, Matrix4x4.Identity) ? null : turn;
            }

            var hips = -1;
            var lift = 1f;

            if (source.HumanBones.TryGetValue("hips", out var fromHips)
                && model.HumanBones.TryGetValue("hips", out var ontoHips)
                && fromHips >= 0 && fromHips < source.Bones.Length
                && ontoHips >= 0 && ontoHips < model.Bones.Length)
            {
                var was = (fromRest[fromHips] * source.Root).Translation.Y;
                var now = (ontoRest[ontoHips] * model.Root).Translation.Y;

                if (MathF.Abs(was) > 1e-4f)
                {
                    hips = fromHips;
                    lift = now / was;
                }
            }

            var built = new List<ModelAnimation>(source.Animations.Length);

            foreach (var animation in source.Animations)
            {
                var slots = new Dictionary<int, int>(animation.Bones.Length);
                var taken = new HashSet<int>();
                var turning = new HashSet<int>();
                var moved = new List<ModelAnimatedBone>(animation.Bones.Length);
                var resting = new Dictionary<int, Vector3>(animation.Bones.Length);

                foreach (var one in animation.Bones)
                {
                    if (one.Bone < 0
                        || one.Bone >= source.Bones.Length
                        || slots.ContainsKey(one.Bone)
                        || Pick(one.Bone) is not { } bone
                        || !taken.Add(bone)
                        || !Matrix4x4.Invert(model.Bones[bone].Local, out var inverse))
                    {
                        continue;
                    }

                    slots[one.Bone] = bone;
                    resting[one.Bone] = one.Translation;

                    var same = !animation.Relative && Alike(inverse, one.RestInverse);
                    var kept = same ? inverse : one.RestInverse;

                    if (!animation.Relative && !same)
                        turning.Add(one.Bone);

                    if (Facing(one.Bone, bone) is { } turn && Matrix4x4.Invert(turn, out var back))
                        moved.Add(one with { Bone = bone, RestInverse = kept * back, Facing = turn });
                    else
                        moved.Add(one with { Bone = bone, RestInverse = kept });
                }

                var tracks = new List<ModelTrack>(animation.Tracks.Length);

                foreach (var track in animation.Tracks)
                {
                    if (track.Kind == ModelTrackKind.Weight)
                    {
                        if (track.Morph >= 0
                            && track.Morph < source.Morphs.Length
                            && morphs.TryGetValue(source.Morphs[track.Morph].Name, out var morph))
                        {
                            tracks.Add(track with { Morph = morph });
                        }

                        continue;
                    }

                    if (!slots.TryGetValue(track.Bone, out var at))
                        continue;

                    if (turning.Contains(track.Bone)
                        && track.Kind is not (ModelTrackKind.Rotation or ModelTrackKind.Angles))
                    {
                        if (track.Kind != ModelTrackKind.Translation
                            || track.Bone != hips
                            || !resting.TryGetValue(track.Bone, out var rest))
                        {
                            continue;
                        }

                        tracks.Add(track with { Bone = at, Values = Lift(track.Values, rest, lift) });

                        continue;
                    }

                    tracks.Add(track with { Bone = at });
                }

                if (tracks.Count > 0)
                    built.Add(animation with { Tracks = [.. tracks], Bones = [.. moved] });
            }

            return [.. built];
        }
    }
}
