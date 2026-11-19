using System.Numerics;
using YMM43D.Commons;

namespace YMM43D.Graphics.Models
{
    public sealed class ModelData(
        Vertex[] vertices,
        uint[] indices,
        ModelPart[] parts,
        ModelImage[] images,
        string[] materials,
        ModelBone[]? bones = null,
        ModelInfluence[]? influences = null,
        Matrix4x4? root = null,
        ModelMorph[]? morphs = null,
        ModelExpression[]? expressions = null,
        ModelAnimation[]? animations = null,
        ModelSpring[]? springs = null)
    {
        public const string NoMaterial = "（材質なし）";

        public string[] Materials { get; } = materials;

        public ModelBone[] Bones { get; } = bones ?? [];

        public ModelInfluence[] Influences { get; } = influences ?? [];

        public Matrix4x4 Root { get; } = root ?? Matrix4x4.Identity;

        public bool HasBones => Bones.Length > 0 && Influences.Length == Vertices.Length;

        public ModelMorph[] Morphs { get; } = morphs ?? [];

        public ModelExpression[] Expressions { get; } = expressions ?? [];

        public ModelAnimation[] Animations { get; } = animations ?? [];

        public bool PrefersUnlit { get; } = parts.Length > 0 && parts.All(part => part.Unlit);

        public ModelSpring[] Springs { get; } = springs ?? [];

        public ModelSpringCollider[] Colliders { get; init; } = [];

        public IReadOnlyDictionary<string, int> HumanBones { get; init; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public ModelIk[] Iks { get; init; } = [];

        public ModelInherit[] Inherits { get; init; } = [];

        public int[] BoneOrder { get; init; } = [];

        public bool IsSolved => Iks.Length > 0 || Inherits.Length > 0;

        public bool HasMorphs => Morphs.Length > 0;

        public bool IsDeformed => HasBones || HasMorphs;

        public Vertex[] Vertices { get; } = vertices;

        public uint[] Indices { get; } = indices;

        public ModelPart[] Parts { get; } = parts;

        public ModelImage[] Images { get; } = images;

        public WorldBounds[] BoneBounds { get; } = MeasureBones(vertices, bones, influences);

        public WorldBounds Bounds { get; } = Measure(
            vertices,
            bones is { Length: > 0 } && influences is { Length: > 0 } && influences.Length == vertices.Length
                ? ModelPose.Skin(bones, root ?? Matrix4x4.Identity)
                : null,
            influences,
            root ?? Matrix4x4.Identity);

        public static Vector3 Place(in Vector3 position, in ModelInfluence influence, IReadOnlyList<Matrix4x4> skin)
        {
            var weights = influence.Weights;

            return weights.X * Vector3.Transform(position, skin[influence.Bone0])
                 + weights.Y * Vector3.Transform(position, skin[influence.Bone1])
                 + weights.Z * Vector3.Transform(position, skin[influence.Bone2])
                 + weights.W * Vector3.Transform(position, skin[influence.Bone3]);
        }

        public WorldBounds PosedBounds(IReadOnlyList<Matrix4x4> skin)
        {
            if (BoneBounds.Length == 0)
                return Bounds;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var found = false;

            for (var i = 0; i < BoneBounds.Length && i < skin.Count; i++)
            {
                if (BoneBounds[i].Max.X < BoneBounds[i].Min.X)
                    continue;

                var moved = BoneBounds[i].Transform(skin[i]);

                min = Vector3.Min(min, moved.Min);
                max = Vector3.Max(max, moved.Max);
                found = true;
            }

            return found ? new WorldBounds(min, max) : Bounds;
        }

        private static WorldBounds[] MeasureBones(Vertex[] vertices, ModelBone[]? bones, ModelInfluence[]? influences)
        {
            if (bones is not { Length: > 0 } || influences is null || influences.Length != vertices.Length)
                return [];

            var min = new Vector3[bones.Length];
            var max = new Vector3[bones.Length];
            var used = new bool[bones.Length];

            for (var i = 0; i < vertices.Length; i++)
            {
                var influence = influences[i];

                if (influence.IsEmpty)
                    continue;

                var weights = influence.Weights;
                var bone = influence.Bone0;
                var best = weights.X;

                if (weights.Y > best) { bone = influence.Bone1; best = weights.Y; }
                if (weights.Z > best) { bone = influence.Bone2; best = weights.Z; }
                if (weights.W > best) { bone = influence.Bone3; }

                if (bone >= bones.Length)
                    continue;

                var position = vertices[i].Position;

                if (!used[bone])
                {
                    used[bone] = true;
                    min[bone] = position;
                    max[bone] = position;
                    continue;
                }

                min[bone] = Vector3.Min(min[bone], position);
                max[bone] = Vector3.Max(max[bone], position);
            }

            var found = new WorldBounds[bones.Length];

            for (var i = 0; i < bones.Length; i++)
                found[i] = used[i] ? new WorldBounds(min[i], max[i]) : new WorldBounds(Vector3.One, Vector3.Zero);

            return found;
        }

        private static WorldBounds Measure(Vertex[] vertices, Matrix4x4[]? skin, ModelInfluence[]? influences, Matrix4x4 root)
        {
            if (vertices.Length == 0)
                return WorldBounds.Empty;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            for (var i = 0; i < vertices.Length; i++)
            {
                var position = skin is null || influences is null || influences[i].IsEmpty
                    ? Vector3.Transform(vertices[i].Position, skin is null ? Matrix4x4.Identity : root)
                    : Place(vertices[i].Position, influences[i], skin);

                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            return new WorldBounds(min, max);
        }
    }
}
