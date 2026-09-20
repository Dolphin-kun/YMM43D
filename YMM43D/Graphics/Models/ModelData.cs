using System.IO;
using System.Numerics;
using Vortice.Mathematics;
using YMM43D.Commons;

namespace YMM43D.Graphics.Models
{
    public readonly record struct ModelPart(int IndexStart, int IndexCount, Vector4 Color, int Image, int Material);

    public sealed record ModelImage(int Width, int Height, byte[] Pixels);

    // 骨。親からの位置関係（Local）と、骨が動いていないときの頂点との関係（InverseBind）を持つ。
    public readonly record struct ModelBone(string Name, int Parent, Matrix4x4 Local, Matrix4x4 InverseBind);

    // 1 つの頂点が、どの骨にどれだけ引っぱられるか。
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
        Matrix4x4? root = null)
    {
        public const string NoMaterial = "（材質なし）";

        public string[] Materials { get; } = materials;

        public ModelBone[] Bones { get; } = bones ?? [];

        // 頂点と同じ並び。骨が無いモデルでは空。
        public ModelInfluence[] Influences { get; } = influences ?? [];

        // 骨で動かした結果に最後に掛ける、モデル全体の向き合わせ。
        public Matrix4x4 Root { get; } = root ?? Matrix4x4.Identity;

        public bool HasBones => Bones.Length > 0 && Influences.Length == vertices.Length;

        public Vertex[] Vertices { get; } = vertices;

        public uint[] Indices { get; } = indices;

        public ModelPart[] Parts { get; } = parts;

        public ModelImage[] Images { get; } = images;

        // 骨ごとに、その骨が主に動かす頂点の範囲。姿勢を変えたときの範囲を出すのに使う。
        public WorldBounds[] BoneBounds { get; } = MeasureBones(vertices, bones, influences);

        // 骨のあるモデルは、頂点が骨の座標系のままなので、骨を掛けたあと（基本の姿勢）で測る。
        public WorldBounds Bounds { get; } = Measure(
            vertices,
            bones is { Length: > 0 } && influences is { Length: > 0 } && influences.Length == vertices.Length
                ? ModelPose.Skin(bones, root ?? Matrix4x4.Identity)
                : null,
            influences,
            root ?? Matrix4x4.Identity);

        // 骨で動かしたあとの頂点の位置。骨が無いモデルではそのままの位置。
        public static Vector3 Place(
            in Vector3 position, in ModelInfluence influence, IReadOnlyList<Matrix4x4> skin)
        {
            var weights = influence.Weights;

            return weights.X * Vector3.Transform(position, skin[influence.Bone0])
                 + weights.Y * Vector3.Transform(position, skin[influence.Bone1])
                 + weights.Z * Vector3.Transform(position, skin[influence.Bone2])
                 + weights.W * Vector3.Transform(position, skin[influence.Bone3]);
        }

        // 今の姿勢で占める範囲。骨ごとの範囲を動かして囲み直す。
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

        // 頂点を、いちばん強く引っぱる骨に割り当てて範囲を測る。
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

        public List<ModelBone> Bones { get; } = [];

        public Matrix4x4 Root { get; set; } = Matrix4x4.Identity;

        public int VertexCount => vertices.Count;

        public int AddImage(ModelImage image)
        {
            images.Add(image);
            return images.Count - 1;
        }

        public void BeginPart(Vector4 color, int image, string key, string name)
        {
            EndPart();
            partColor = color;
            partImage = image;

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

            return new ModelData(
                [.. vertices],
                [.. indices],
                renumbered,
                [.. images],
                [.. used.Select(index => materials[index])],
                skinned ? [.. Bones] : null,
                skinned ? [.. influences] : null,
                Root);
        }

        private void EndPart()
        {
            if (indices.Count > partStart)
                parts.Add(new ModelPart(partStart, indices.Count - partStart, partColor, partImage, partMaterial));

            partStart = indices.Count;
        }
    }
}
