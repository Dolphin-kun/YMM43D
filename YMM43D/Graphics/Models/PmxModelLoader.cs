using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;

namespace YMM43D.Graphics.Models
{
    public static class PmxModelLoader
    {
        public const float Scale = 0.08f;

        public static ModelData Load(string path)
        {
            var full = Path.GetFullPath(path);

            return new Reader(File.ReadAllBytes(full), Path.GetDirectoryName(full) ?? string.Empty).Read();
        }

        private sealed class Reader(byte[] data, string directory)
        {
            private readonly ModelBuilder builder = new();

            private int at;
            private bool wide;
            private int extraUvs;
            private int vertexSize;
            private int textureSize;
            private int materialSize;
            private int boneSize;
            private int morphSize;
            private int bodySize;

            private readonly List<string> texturePaths = [];
            private readonly Dictionary<int, int> images = [];
            private readonly List<Vector3> bonePlaces = [];

            public ModelData Read()
            {
                builder.Root = Matrix4x4.CreateScale(Scale);

                ReadHeader();
                ReadVertices();

                var indices = ReadIndices();

                ReadTextures();
                ReadMaterials(indices);
                ReadBones();
                ReadMorphs();

                try
                {
                    ReadPhysics();
                }
                catch (Exception error) when (error is InvalidDataException or ArgumentException)
                {
                    Trace.TraceWarning($"[YMM43D] pmx の剛体を読めなかったので、揺れものなしで読み込みます。{error.Message}");
                }

                return builder.Build();
            }

            private void ReadHeader()
            {
                if (data.Length < 36 || Encoding.ASCII.GetString(data, 0, 4) != "PMX ")
                {
                    throw new InvalidDataException("pmx のしるしがありません。pmd には対応していないので、PmxEditor で pmx にしてください。");
                }

                at = 4;

                var version = Single();

                if (version < 2f)
                    throw new InvalidDataException($"pmx {version:F1} には対応していません。2.0 以降にしてください。");

                var count = data[at++];

                if (count < 8 || at + count > data.Length)
                    throw new InvalidDataException("pmx の設定が足りません。");

                wide = data[at] == 0;
                extraUvs = data[at + 1];
                vertexSize = data[at + 2];
                textureSize = data[at + 3];
                materialSize = data[at + 4];
                boneSize = data[at + 5];
                morphSize = data[at + 6];
                bodySize = data[at + 7];

                at += count;

                for (var i = 0; i < 4; i++)
                    Text();
            }

            private void ReadVertices()
            {
                var count = Integer();

                if (count < 0)
                    throw new InvalidDataException("pmx の頂点の数がおかしいです。");

                for (var i = 0; i < count; i++)
                {
                    var position = Place();
                    var normal = Place();
                    var uv = new Vector2(Single(), Single());

                    at += extraUvs * 16;

                    var influence = Weights();

                    Single();

                    builder.AddVertex(position, normal, uv, Vector4.One, influence);
                }
            }

            private ModelInfluence Weights()
            {
                switch (data[at++])
                {
                    case 0:
                        return new ModelInfluence(Bone(), 0, 0, 0, new Vector4(1f, 0f, 0f, 0f));

                    case 1:
                        {
                            var first = Bone();
                            var second = Bone();
                            var weight = Single();

                            return Pick(first, second, 0, 0, new Vector4(weight, 1f - weight, 0f, 0f));
                        }

                    case 2:
                    case 4:
                        {
                            var one = Bone();
                            var two = Bone();
                            var three = Bone();
                            var four = Bone();

                            return Pick(one, two, three, four,
                                new Vector4(Single(), Single(), Single(), Single()));
                        }

                    case 3:
                        {
                            var first = Bone();
                            var second = Bone();
                            var weight = Single();

                            at += 36;

                            return Pick(first, second, 0, 0, new Vector4(weight, 1f - weight, 0f, 0f));
                        }

                    default:
                        throw new InvalidDataException("pmx の重みの付け方に対応していません。");
                }
            }

            private static ModelInfluence Pick(ushort one, ushort two, ushort three, ushort four, in Vector4 weights)
            {
                var total = weights.X + weights.Y + weights.Z + weights.W;

                return total > 1e-6f
                    ? new ModelInfluence(one, two, three, four, weights / total)
                    : new ModelInfluence(one, 0, 0, 0, new Vector4(1f, 0f, 0f, 0f));
            }

            private int[] ReadIndices()
            {
                var count = Integer();
                var made = new int[Math.Max(count, 0)];

                for (var i = 0; i < made.Length; i++)
                    made[i] = VertexAt();

                return made;
            }

            private void ReadTextures()
            {
                var count = Integer();

                for (var i = 0; i < count; i++)
                    texturePaths.Add(Text());
            }

            private void ReadMaterials(int[] indices)
            {
                var count = Integer();
                var start = 0;

                for (var i = 0; i < count; i++)
                {
                    var name = Text();

                    Text();

                    var diffuse = new Vector4(Single(), Single(), Single(), Single());

                    at += 12;

                    var shine = Single();

                    at += 12;

                    at += 1;
                    at += 20;

                    var texture = Index(textureSize);

                    Index(textureSize);

                    at += 1;

                    if (data[at++] == 1)
                        at += 1;
                    else
                        Index(textureSize);

                    Text();

                    var span = Integer();

                    builder.BeginPart(
                        diffuse,
                        ImageOf(texture),
                        $"#{i}",
                        name.Length > 0 ? name : ModelData.NoMaterial,
                        diffuse.W < 0.99f ? ModelBlend.Blend : ModelBlend.Mask,
                        0.5f,
                        false,
                        ModelSurface.Default with { Roughness = shine > 0f ? Math.Clamp(1f - shine / 100f, 0.05f, 1f) : 1f });

                    for (var k = start; k + 2 < start + span && k + 2 < indices.Length; k += 3)
                        builder.AddTriangle((uint)indices[k], (uint)indices[k + 2], (uint)indices[k + 1]);

                    start += span;
                }
            }

            private int ImageOf(int texture)
            {
                if (texture < 0 || texture >= texturePaths.Count)
                    return -1;

                if (images.TryGetValue(texture, out var known))
                    return known;

                var name = texturePaths[texture].Replace('\\', Path.DirectorySeparatorChar);
                var index = -1;

                try
                {
                    var path = Path.GetFullPath(Path.Combine(directory, name));

                    if (File.Exists(path))
                        index = builder.AddImage(ModelImageDecoder.DecodeFile(path));
                    else
                        Trace.TraceWarning($"[YMM43D] pmx の画像 {name} が見つかりません。");
                }
                catch (Exception error)
                {
                    Trace.TraceWarning($"[YMM43D] pmx の画像 {name} を読み込めませんでした。{error.Message}");
                }

                return images[texture] = index;
            }

            private void ReadBones()
            {
                var count = Integer();
                var parents = new int[Math.Max(count, 0)];
                var layers = new int[parents.Length];

                for (var i = 0; i < count; i++)
                {
                    var name = Text();

                    Text();

                    var place = Place();
                    var parent = Index(boneSize);
                    var layer = Integer();
                    var flags = (ushort)(data[at] | (data[at + 1] << 8));

                    at += 2;

                    if ((flags & 0x0001) != 0)
                        Index(boneSize);
                    else
                        at += 12;

                    if ((flags & 0x0300) != 0)
                    {
                        var from = Index(boneSize);
                        var weight = Single();

                        if (from >= 0 && from < count && from != i)
                        {
                            builder.Inherits.Add(new ModelInherit(i, from, weight, (flags & 0x0100) != 0, (flags & 0x0200) != 0));
                        }
                    }

                    if ((flags & 0x0400) != 0)
                        at += 12;

                    if ((flags & 0x0800) != 0)
                        at += 24;

                    if ((flags & 0x2000) != 0)
                        at += 4;

                    if ((flags & 0x0020) != 0)
                        ReadIk(i, count);

                    bonePlaces.Add(place);
                    parents[i] = parent >= 0 && parent < count && parent != i ? parent : -1;
                    layers[i] = layer;

                    builder.Bones.Add(new ModelBone(name, parents[i], Matrix4x4.Identity, Matrix4x4.Identity));
                }

                for (var i = 0; i < count; i++)
                {
                    var parent = parents[i];
                    var local = parent >= 0 ? bonePlaces[i] - bonePlaces[parent] : bonePlaces[i];

                    builder.Bones[i] = builder.Bones[i] with
                    {
                        Local = Matrix4x4.CreateTranslation(local),
                        InverseBind = Matrix4x4.CreateTranslation(-bonePlaces[i]),
                    };
                }

                foreach (var index in Enumerable.Range(0, count)
                    .OrderBy(one => layers[one])
                    .ThenBy(one => one))
                {
                    builder.BoneOrder.Add(index);
                }
            }

            private void ReadIk(int bone, int bones)
            {
                var target = Index(boneSize);
                var loops = Integer();
                var limit = Single();
                var count = Integer();
                var links = new List<ModelIkLink>(Math.Max(count, 0));

                for (var i = 0; i < count; i++)
                {
                    var link = Index(boneSize);
                    var limited = data[at++] != 0;
                    var lower = limited ? Raw() : Vector3.Zero;
                    var upper = limited ? Raw() : Vector3.Zero;

                    if (link < 0 || link >= bones)
                        continue;

                    links.Add(limited
                        ? new ModelIkLink(link, true, new Vector3(-upper.X, -upper.Y, lower.Z), new Vector3(-lower.X, -lower.Y, upper.Z))
                        : new ModelIkLink(link, false, Vector3.Zero, Vector3.Zero));
                }

                if (target >= 0 && target < bones && links.Count > 0)
                    builder.Iks.Add(new ModelIk(bone, target, Math.Clamp(loops, 1, 64), limit, [.. links]));
            }

            private void ReadMorphs()
            {
                var count = Integer();

                for (var i = 0; i < count; i++)
                {
                    var name = Text();

                    Text();

                    at += 1;

                    var kind = data[at++];
                    var offsets = Integer();
                    var deltas = kind == 1 ? new List<ModelMorphDelta>(Math.Max(offsets, 0)) : null;

                    for (var k = 0; k < offsets; k++)
                    {
                        switch (kind)
                        {
                            case 0:
                            case 9:
                                Index(morphSize);
                                at += 4;
                                break;

                            case 1:
                                {
                                    var vertex = VertexAt();
                                    var offset = Place();

                                    if (vertex >= 0 && offset != Vector3.Zero)
                                        deltas?.Add(new ModelMorphDelta((uint)vertex, offset));

                                    break;
                                }

                            case 2:
                                Index(boneSize);
                                at += 28;
                                break;

                            case 3:
                            case 4:
                            case 5:
                            case 6:
                            case 7:
                                VertexAt();
                                at += 16;
                                break;

                            case 8:
                                Index(materialSize);
                                at += 113;
                                break;

                            case 10:
                                Index(bodySize);
                                at += 25;
                                break;

                            default:
                                throw new InvalidDataException($"pmx のモーフ {kind} に対応していません。");
                        }
                    }

                    if (deltas is not { Count: > 0 } || name.Length == 0)
                        continue;

                    builder.Morphs.Add(new ModelMorph(name, [.. deltas]));
                    builder.Expressions.Add(new ModelExpression(name, string.Empty, [new ModelExpressionBind(builder.Morphs.Count - 1, 1f)]));
                }
            }

            private void ReadPhysics()
            {
                if (at + 4 > data.Length)
                    return;

                var frames = Integer();

                for (var i = 0; i < frames; i++)
                {
                    Text();
                    Text();

                    at += 1;

                    var elements = Integer();

                    for (var k = 0; k < elements; k++)
                    {
                        var kind = data[at++];

                        Index(kind == 0 ? boneSize : morphSize);
                    }
                }

                if (at + 4 > data.Length)
                    return;

                var count = Integer();
                var moving = new HashSet<int>();
                var colliders = new List<ModelSpringCollider>();

                for (var i = 0; i < count; i++)
                {
                    Text();
                    Text();

                    var bone = Index(boneSize);

                    at += 3;

                    var shape = data[at++];
                    var size = Raw();
                    var position = Place();
                    var rotation = Raw();

                    at += 20;

                    var mode = data[at++];

                    if (bone < 0 || bone >= bonePlaces.Count)
                        continue;

                    if (mode != 0)
                        moving.Add(bone);
                    else if (Collider(bone, shape, size, position, rotation) is { } collider)
                        colliders.Add(collider);
                }

                builder.Colliders.AddRange(colliders);

                ModelBone[] bones = [.. builder.Bones];

                foreach (var root in moving.Where(bone => bones[bone].Parent < 0 || !moving.Contains(bones[bone].Parent)).Order())
                {
                    if (ModelSpring.From(bones, root, colliders: [.. colliders]) is { } spring)
                        builder.Springs.Add(spring);
                }
            }

            private ModelSpringCollider? Collider(int bone, byte shape, Vector3 size, Vector3 position, Vector3 rotation)
            {
                var offset = position - bonePlaces[bone];

                switch (shape)
                {
                    case 0 when size.X > 0f:
                        return new ModelSpringCollider(bone, offset, size.X, Vector3.Zero, false);

                    case 2 when size.X > 0f:
                        {
                            var turned = Vector3.TransformNormal(Vector3.UnitY, Matrix4x4.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z));
                            var half = new Vector3(turned.X, turned.Y, -turned.Z) * (size.Y / 2f);

                            return new ModelSpringCollider(bone, offset - half, size.X, offset + half, true);
                        }

                    default:
                        return null;
                }
            }

            private string Text()
            {
                var length = Integer();

                if (length < 0 || at + length > data.Length)
                    throw new InvalidDataException("pmx の文字列が壊れています。");

                var value = wide
                    ? Encoding.Unicode.GetString(data, at, length)
                    : Encoding.UTF8.GetString(data, at, length);

                at += length;

                return value;
            }

            private float Single()
            {
                var value = BitConverter.ToSingle(data, at);
                at += 4;

                return value;
            }

            private int Integer()
            {
                var value = BitConverter.ToInt32(data, at);
                at += 4;

                return value;
            }

            private Vector3 Raw() => new(Single(), Single(), Single());

            private Vector3 Place()
            {
                var place = Raw();

                return new Vector3(place.X, place.Y, -place.Z);
            }

            private int Index(int size) => size switch
            {
                1 => (sbyte)data[at++],
                2 => Short(),
                _ => Integer(),
            };

            private int Short()
            {
                var value = BitConverter.ToInt16(data, at);
                at += 2;

                return value;
            }

            private int VertexAt()
            {
                switch (vertexSize)
                {
                    case 1:
                        return data[at++];

                    case 2:
                        {
                            var value = BitConverter.ToUInt16(data, at);
                            at += 2;

                            return value;
                        }

                    default:
                        return Integer();
                }
            }

            private ushort Bone()
            {
                var value = Index(boneSize);

                return value >= 0 ? (ushort)value : (ushort)0;
            }
        }
    }
}
