using System.IO;
using System.Numerics;
using Vortice.Mathematics;
using YMM43D.Commons;

namespace YMM43D.Graphics.Models
{
    public enum ModelBlend
    {
        Opaque,

        Mask,

        Blend,
    }

    public readonly record struct ModelPart(
        int IndexStart,
        int IndexCount,
        Vector4 Color,
        int Image,
        int Material,
        ModelBlend Blend,
        float Cutoff,
        Vector3 Center,
        bool Unlit = false)
    {
        private readonly ModelSurface? surface;

        public ModelSurface Surface
        {
            get => surface ?? ModelSurface.Default;
            init => surface = value;
        }
    }

    public sealed record ModelImage(int Width, int Height, byte[] Pixels);

    public readonly record struct ModelMorphDelta(uint Vertex, Vector3 Offset);

    public sealed record ModelMorph(string Name, ModelMorphDelta[] Deltas);

    public readonly record struct ModelExpressionBind(int Morph, float Weight);

    public sealed record ModelExpression(string Name, string Preset, ModelExpressionBind[] Binds);

    public readonly record struct ModelBone(string Name, int Parent, Matrix4x4 Local, Matrix4x4 InverseBind);

    public readonly record struct ModelInfluence(ushort Bone0, ushort Bone1, ushort Bone2, ushort Bone3, Vector4 Weights)
    {
        public static ModelInfluence None => new(0, 0, 0, 0, Vector4.Zero);

        public bool IsEmpty => Weights == Vector4.Zero;
    }

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

        public bool HasAnimations => Animations.Length > 0;

        public bool PrefersUnlit { get; } = parts.Length > 0 && parts.All(part => part.Unlit);

        public ModelSpring[] Springs { get; } = springs ?? [];

        public ModelSpringCollider[] Colliders { get; init; } = [];

        public bool HasSprings => Springs.Length > 0;

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

        public static Vector3 Place(
            in Vector3 position, in ModelInfluence influence, IReadOnlyList<Matrix4x4> skin)
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

        private static WorldBounds[] MeasureBones(
            Vertex[] vertices, ModelBone[]? bones, ModelInfluence[]? influences)
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

        private static WorldBounds Measure(
            Vertex[] vertices, Matrix4x4[]? skin, ModelInfluence[]? influences, Matrix4x4 root)
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

    internal sealed class ModelBuilder
    {
        private readonly List<Vertex> vertices = [];
        private readonly List<uint> indices = [];
        private readonly List<ModelPart> parts = [];
        private readonly List<ModelImage> images = [];
        private readonly List<string> materials = [];
        private readonly Dictionary<string, int> materialKeys = new(StringComparer.Ordinal);

        private readonly List<ModelInfluence> influences = [];

        private int partStart;
        private Vector4 partColor = Vector4.One;
        private int partImage = -1;
        private int partMaterial = -1;
        private ModelBlend partBlend = ModelBlend.Opaque;
        private float partCutoff = 0.5f;
        private bool partUnlit;
        private ModelSurface partSurface = ModelSurface.Default;

        public List<ModelBone> Bones { get; } = [];

        public List<ModelMorph> Morphs { get; } = [];

        public List<ModelExpression> Expressions { get; } = [];

        public List<ModelAnimation> Animations { get; } = [];

        public List<ModelSpring> Springs { get; } = [];

        public List<ModelSpringCollider> Colliders { get; } = [];

        public const float MorphThreshold = 1e-5f;

        public Matrix4x4 Root { get; set; } = Matrix4x4.Identity;

        public int VertexCount => vertices.Count;

        public int AddImage(ModelImage image)
        {
            images.Add(image);
            return images.Count - 1;
        }

        public void BeginPart(
            Vector4 color,
            int image,
            string key,
            string name,
            ModelBlend blend = ModelBlend.Opaque,
            float cutoff = 0.5f,
            bool unlit = false,
            ModelSurface? surface = null)
        {
            EndPart();
            partColor = color;
            partImage = image;
            partBlend = blend;
            partCutoff = cutoff;
            partUnlit = unlit;
            partSurface = surface ?? ModelSurface.Default;

            if (!materialKeys.TryGetValue(key, out partMaterial))
            {
                materials.Add(name);
                partMaterial = materialKeys[key] = materials.Count - 1;
            }
        }

        public uint AddVertex(Vector3 position, Vector3 normal, Vector2 texCoord, Vector4 color)
            => AddVertex(position, normal, texCoord, color, ModelInfluence.None);

        public uint AddVertex(
            Vector3 position, Vector3 normal, Vector2 texCoord, Vector4 color, in ModelInfluence influence)
        {
            vertices.Add(new Vertex(position, new Color4(color), texCoord, normal));
            influences.Add(influence);
            return (uint)(vertices.Count - 1);
        }

        public void AddTriangle(uint a, uint b, uint c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        public Vector3 PositionOf(uint index) => vertices[(int)index].Position;

        public void SetNormal(uint index, Vector3 normal)
        {
            var vertex = vertices[(int)index];
            vertex.Normal = normal;
            vertices[(int)index] = vertex;
        }

        public ModelData Build()
        {
            EndPart();

            if (indices.Count == 0)
                throw new InvalidDataException("三角形が1つもありません。");

            var used = parts.Select(part => part.Material).Distinct().Order().ToArray();
            var renumbered = parts
                .Select(part => part with { Material = Array.IndexOf(used, part.Material) })
                .ToArray();

            var skinned = Bones.Count > 0 && influences.Any(influence => !influence.IsEmpty);

            if (skinned)
                HoldStillVertices();

            if (parts.Any(part => part.Surface.HasBump))
                FillTangents();

            return new ModelData(
                [.. vertices],
                [.. indices],
                renumbered,
                [.. images],
                [.. used.Select(index => materials[index])],
                skinned ? [.. Bones] : null,
                skinned ? [.. influences] : null,
                Root,
                [.. Morphs],
                [.. Expressions],
                [.. Animations],
                [.. Springs])
            {
                Colliders = [.. Colliders],
            };
        }

        private void HoldStillVertices()
        {
            var rest = -1;

            for (var i = 0; i < influences.Count; i++)
            {
                if (!influences[i].IsEmpty)
                    continue;

                if (rest < 0)
                {
                    Bones.Add(new ModelBone("（動かないところ）", -1, Matrix4x4.Identity, Matrix4x4.Identity));
                    rest = Bones.Count - 1;
                }

                influences[i] = new ModelInfluence((ushort)rest, 0, 0, 0, new Vector4(1f, 0f, 0f, 0f));
            }
        }

        private void EndPart()
        {
            if (indices.Count > partStart)
            {
                parts.Add(new ModelPart(
                    partStart,
                    indices.Count - partStart,
                    partColor,
                    partImage,
                    partMaterial,
                    partBlend,
                    partCutoff,
                    CenterOf(partStart, indices.Count - partStart),
                    partUnlit)
                {
                    Surface = partSurface,
                });
            }

            partStart = indices.Count;
        }

        private void FillTangents()
        {
            var along = new Vector3[vertices.Count];
            var across = new Vector3[vertices.Count];

            for (var i = 0; i + 2 < indices.Count; i += 3)
            {
                var ia = (int)indices[i];
                var ib = (int)indices[i + 1];
                var ic = (int)indices[i + 2];

                var a = vertices[ia];
                var b = vertices[ib];
                var c = vertices[ic];

                var edge1 = b.Position - a.Position;
                var edge2 = c.Position - a.Position;

                var uv1 = b.TexCoord - a.TexCoord;
                var uv2 = c.TexCoord - a.TexCoord;

                var area = uv1.X * uv2.Y - uv2.X * uv1.Y;

                if (MathF.Abs(area) < 1e-12f)
                    continue;

                var scale = 1f / area;
                var tangent = (edge1 * uv2.Y - edge2 * uv1.Y) * scale;
                var other = (edge2 * uv1.X - edge1 * uv2.X) * scale;

                along[ia] += tangent;
                along[ib] += tangent;
                along[ic] += tangent;

                across[ia] += other;
                across[ib] += other;
                across[ic] += other;
            }

            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];

                if (along[i].LengthSquared() < 1e-20f || vertex.Normal.LengthSquared() < 1e-20f)
                    continue;

                var normal = Vector3.Normalize(vertex.Normal);

                var tangent = along[i] - normal * Vector3.Dot(normal, along[i]);

                if (tangent.LengthSquared() < 1e-20f)
                    continue;

                tangent = Vector3.Normalize(tangent);

                var hand = Vector3.Dot(Vector3.Cross(normal, tangent), across[i]) < 0f ? -1f : 1f;

                vertex.Tangent = new Vector4(tangent, hand);
                vertices[i] = vertex;
            }
        }

        private Vector3 CenterOf(int start, int count)
        {
            if (count == 0)
                return Vector3.Zero;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            for (var i = start; i < start + count; i++)
            {
                var position = vertices[(int)indices[i]].Position;

                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            return (min + max) * 0.5f;
        }
    }
}
