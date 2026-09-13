using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace YMM43D.Graphics.Models
{
    public static class GltfModelLoader
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinaryChunk = 0x004E4942;

        private const int TriangleMode = 4;

        private static readonly string[] UnsupportedExtensions =
        [
            "KHR_draco_mesh_compression",
            "EXT_meshopt_compression",
            "KHR_mesh_quantization",
        ];

        public static ModelData Load(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
            var bytes = File.ReadAllBytes(path);

            var (json, embedded) = IsGlb(bytes) ? SplitGlb(bytes) : (bytes, null);

            using var document = JsonDocument.Parse(json);

            return new Reader(document.RootElement, directory, embedded).Read();
        }

        private static bool IsGlb(byte[] bytes)
            => bytes.Length >= 12 && BitConverter.ToUInt32(bytes, 0) == GlbMagic;

        private static (byte[] Json, byte[]? Binary) SplitGlb(byte[] bytes)
        {
            byte[]? json = null;
            byte[]? binary = null;

            var at = 12;

            while (at + 8 <= bytes.Length)
            {
                var length = (int)BitConverter.ToUInt32(bytes, at);
                var type = BitConverter.ToUInt32(bytes, at + 4);
                var start = at + 8;

                if (start + length > bytes.Length)
                    throw new InvalidDataException("glb のチャンクがファイルの終わりを越えています。");

                if (type == JsonChunk && json is null)
                    json = bytes[start..(start + length)];
                else if (type == BinaryChunk && binary is null)
                    binary = bytes[start..(start + length)];

                at = start + length;
            }

            return (json ?? throw new InvalidDataException("glb に JSON チャンクがありません。"), binary);
        }

        private sealed class Reader(JsonElement root, string directory, byte[]? embedded)
        {
            private readonly ModelBuilder builder = new();
            private readonly Dictionary<int, byte[]> buffers = [];
            private readonly Dictionary<int, int> images = [];

            public ModelData Read()
            {
                RejectUnsupported();

                foreach (var node in RootNodes())
                    Visit(node, Matrix4x4.Identity, 0);

                return builder.Build();
            }

            private void RejectUnsupported()
            {
                if (!root.TryGetProperty("extensionsRequired", out var required))
                    return;

                foreach (var extension in required.EnumerateArray())
                {
                    var name = extension.GetString();

                    if (name is not null && UnsupportedExtensions.Contains(name))
                        throw new NotSupportedException($"圧縮された glTF（{name}）には対応していません。");
                }
            }

            private IEnumerable<int> RootNodes()
            {
                if (root.TryGetProperty("scenes", out var scenes) && scenes.GetArrayLength() > 0)
                {
                    var index = root.TryGetProperty("scene", out var scene) ? scene.GetInt32() : 0;

                    if (scenes[index].TryGetProperty("nodes", out var nodes))
                        return nodes.EnumerateArray().Select(node => node.GetInt32()).ToArray();

                    return [];
                }

                if (!root.TryGetProperty("nodes", out var all))
                    return [];

                var children = new HashSet<int>();

                foreach (var node in all.EnumerateArray())
                {
                    if (node.TryGetProperty("children", out var list))
                    {
                        foreach (var child in list.EnumerateArray())
                            children.Add(child.GetInt32());
                    }
                }

                return Enumerable.Range(0, all.GetArrayLength()).Where(index => !children.Contains(index)).ToArray();
            }

            private void Visit(int index, in Matrix4x4 parent, int depth)
            {
                if (depth > 64)
                    throw new InvalidDataException("ノードの入れ子が深すぎます。");

                var node = root.GetProperty("nodes")[index];
                var world = LocalMatrix(node) * parent;

                if (node.TryGetProperty("mesh", out var mesh))
                    AddMesh(root.GetProperty("meshes")[mesh.GetInt32()], world);

                if (node.TryGetProperty("children", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                        Visit(child.GetInt32(), world, depth + 1);
                }
            }

            private static Matrix4x4 LocalMatrix(JsonElement node)
            {
                if (node.TryGetProperty("matrix", out var matrix))
                {
                    var m = Floats(matrix, 16);

                    return new Matrix4x4(
                        m[0], m[1], m[2], m[3],
                        m[4], m[5], m[6], m[7],
                        m[8], m[9], m[10], m[11],
                        m[12], m[13], m[14], m[15]);
                }

                var scale = node.TryGetProperty("scale", out var s) ? Floats(s, 3) : [1f, 1f, 1f];
                var rotation = node.TryGetProperty("rotation", out var r) ? Floats(r, 4) : [0f, 0f, 0f, 1f];
                var translation = node.TryGetProperty("translation", out var t) ? Floats(t, 3) : [0f, 0f, 0f];

                return Matrix4x4.CreateScale(scale[0], scale[1], scale[2])
                     * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3])))
                     * Matrix4x4.CreateTranslation(translation[0], translation[1], translation[2]);
            }

            private void AddMesh(JsonElement mesh, in Matrix4x4 world)
            {
                Matrix4x4.Invert(world, out var inverse);
                var normalMatrix = Matrix4x4.Transpose(inverse);

                foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
                {
                    if (primitive.TryGetProperty("mode", out var mode) && mode.GetInt32() != TriangleMode)
                        continue;

                    var attributes = primitive.GetProperty("attributes");

                    if (!attributes.TryGetProperty("POSITION", out var positionAccessor))
                        continue;

                    var positions = ReadVectors(positionAccessor.GetInt32(), 3);
                    var count = positions.Length / 3;

                    var normals = attributes.TryGetProperty("NORMAL", out var n) ? ReadVectors(n.GetInt32(), 3) : null;
                    var texCoords = attributes.TryGetProperty("TEXCOORD_0", out var uv) ? ReadVectors(uv.GetInt32(), 2) : null;
                    var colors = attributes.TryGetProperty("COLOR_0", out var c) ? ReadColors(c.GetInt32()) : null;

                    var indices = primitive.TryGetProperty("indices", out var i)
                        ? ReadIndices(i.GetInt32())
                        : [.. Enumerable.Range(0, count).Select(index => (uint)index)];

                    var (color, image) = ReadMaterial(primitive);

                    builder.BeginPart(color, image);

                    var start = (uint)builder.VertexCount;

                    for (var v = 0; v < count; v++)
                    {
                        var position = Vector3.Transform(new Vector3(positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2]), world);

                        var normal = normals is null
                            ? Vector3.Zero
                            : Vector3.TransformNormal(new Vector3(normals[v * 3], normals[v * 3 + 1], normals[v * 3 + 2]), normalMatrix);

                        builder.AddVertex(
                            position,
                            normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.Zero,
                            texCoords is null ? Vector2.Zero : new Vector2(texCoords[v * 2], texCoords[v * 2 + 1]),
                            colors?[v] ?? Vector4.One);
                    }

                    for (var t = 0; t + 2 < indices.Length; t += 3)
                    {
                        if (indices[t] >= count || indices[t + 1] >= count || indices[t + 2] >= count)
                            throw new InvalidDataException("頂点番号が頂点の数を越えています。");

                        var a = start + indices[t];
                        var b = start + indices[t + 1];
                        var d = start + indices[t + 2];

                        builder.AddTriangle(a, b, d);

                        if (normals is null)
                            FillFlatNormal(a, b, d);
                    }
                }
            }

            private void FillFlatNormal(uint a, uint b, uint c)
            {
                var normal = Vector3.Cross(builder.PositionOf(b) - builder.PositionOf(a), builder.PositionOf(c) - builder.PositionOf(a));

                if (normal.LengthSquared() <= 1e-20f)
                    return;

                normal = Vector3.Normalize(normal);

                builder.SetNormal(a, normal);
                builder.SetNormal(b, normal);
                builder.SetNormal(c, normal);
            }

            private (Vector4 Color, int Image) ReadMaterial(JsonElement primitive)
            {
                if (!primitive.TryGetProperty("material", out var index) || !root.TryGetProperty("materials", out var materials))
                    return (Vector4.One, -1);

                var material = materials[index.GetInt32()];

                if (!material.TryGetProperty("pbrMetallicRoughness", out var pbr))
                    return (Vector4.One, -1);

                var factor = pbr.TryGetProperty("baseColorFactor", out var f) ? Floats(f, 4) : [1f, 1f, 1f, 1f];
                var color = new Vector4(factor[0], factor[1], factor[2], factor[3]);

                if (!pbr.TryGetProperty("baseColorTexture", out var texture))
                    return (color, -1);

                return (color, ImageOfTexture(texture.GetProperty("index").GetInt32()));
            }

            private int ImageOfTexture(int textureIndex)
            {
                if (!root.TryGetProperty("textures", out var textures))
                    return -1;

                var texture = textures[textureIndex];

                if (!texture.TryGetProperty("source", out var source))
                    return -1;

                var imageIndex = source.GetInt32();

                if (images.TryGetValue(imageIndex, out var known))
                    return known;

                try
                {
                    var image = root.GetProperty("images")[imageIndex];

                    var data = image.TryGetProperty("bufferView", out var view)
                        ? ReadBufferView(view.GetInt32())
                        : ReadUri(image.GetProperty("uri").GetString() ?? string.Empty);

                    return images[imageIndex] = builder.AddImage(ModelImageDecoder.Decode(data));
                }
                catch (Exception)
                {
                    return images[imageIndex] = -1;
                }
            }

            private float[] ReadVectors(int accessorIndex, int width)
            {
                var accessor = root.GetProperty("accessors")[accessorIndex];
                var values = ReadAccessor(accessor, out var components);

                if (components != width)
                    throw new InvalidDataException($"要素の数が {components} で、{width} を期待していました。");

                return values;
            }

            private Vector4[] ReadColors(int accessorIndex)
            {
                var accessor = root.GetProperty("accessors")[accessorIndex];
                var values = ReadAccessor(accessor, out var components);
                var count = values.Length / components;
                var colors = new Vector4[count];

                for (var i = 0; i < count; i++)
                {
                    var at = i * components;

                    colors[i] = new Vector4(values[at], values[at + 1], values[at + 2], components == 4 ? values[at + 3] : 1f);
                }

                return colors;
            }

            private uint[] ReadIndices(int accessorIndex)
            {
                var accessor = root.GetProperty("accessors")[accessorIndex];
                var count = accessor.GetProperty("count").GetInt32();
                var type = accessor.GetProperty("componentType").GetInt32();
                var size = ComponentSize(type);

                var (data, offset, stride) = Locate(accessor, size);
                var indices = new uint[count];

                for (var i = 0; i < count; i++)
                {
                    var at = offset + i * stride;

                    indices[i] = type switch
                    {
                        5121 => data[at],
                        5123 => BitConverter.ToUInt16(data, at),
                        5125 => BitConverter.ToUInt32(data, at),
                        _ => throw new InvalidDataException($"頂点番号の型 {type} には対応していません。"),
                    };
                }

                return indices;
            }

            private float[] ReadAccessor(JsonElement accessor, out int components)
            {
                if (accessor.TryGetProperty("sparse", out _))
                    throw new NotSupportedException("sparse な accessor には対応していません。");

                components = accessor.GetProperty("type").GetString() switch
                {
                    "SCALAR" => 1,
                    "VEC2" => 2,
                    "VEC3" => 3,
                    "VEC4" => 4,
                    var other => throw new InvalidDataException($"accessor の型 {other} には対応していません。"),
                };

                var count = accessor.GetProperty("count").GetInt32();
                var type = accessor.GetProperty("componentType").GetInt32();
                var normalized = accessor.TryGetProperty("normalized", out var flag) && flag.GetBoolean();
                var size = ComponentSize(type);
                var values = new float[count * components];

                if (!accessor.TryGetProperty("bufferView", out _))
                    return values;

                var (data, offset, stride) = Locate(accessor, size * components);

                for (var i = 0; i < count; i++)
                {
                    for (var c = 0; c < components; c++)
                    {
                        var at = offset + i * stride + c * size;

                        values[i * components + c] = type switch
                        {
                            5126 => BitConverter.ToSingle(data, at),
                            5121 => normalized ? data[at] / 255f : data[at],
                            5123 => normalized ? BitConverter.ToUInt16(data, at) / 65535f : BitConverter.ToUInt16(data, at),
                            5120 => normalized ? MathF.Max((sbyte)data[at] / 127f, -1f) : (sbyte)data[at],
                            5122 => normalized ? MathF.Max(BitConverter.ToInt16(data, at) / 32767f, -1f) : BitConverter.ToInt16(data, at),
                            5125 => BitConverter.ToUInt32(data, at),
                            _ => throw new InvalidDataException($"要素の型 {type} には対応していません。"),
                        };
                    }
                }

                return values;
            }

            private (byte[] Data, int Offset, int Stride) Locate(JsonElement accessor, int elementSize)
            {
                var viewIndex = accessor.GetProperty("bufferView").GetInt32();
                var view = root.GetProperty("bufferViews")[viewIndex];

                var data = Buffer(view.GetProperty("buffer").GetInt32());
                var offset = (view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0)
                           + (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
                var stride = view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : elementSize;

                var count = accessor.GetProperty("count").GetInt32();

                if (count > 0 && offset + (count - 1) * stride + elementSize > data.Length)
                    throw new InvalidDataException("accessor がバッファの終わりを越えています。");

                return (data, offset, stride);
            }

            private byte[] ReadBufferView(int index)
            {
                var view = root.GetProperty("bufferViews")[index];
                var data = Buffer(view.GetProperty("buffer").GetInt32());
                var offset = view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
                var length = view.GetProperty("byteLength").GetInt32();

                return data[offset..(offset + length)];
            }

            private byte[] Buffer(int index)
            {
                if (buffers.TryGetValue(index, out var known))
                    return known;

                var buffer = root.GetProperty("buffers")[index];

                var data = buffer.TryGetProperty("uri", out var uri)
                    ? ReadUri(uri.GetString() ?? string.Empty)
                    : embedded ?? throw new InvalidDataException("glb のバイナリチャンクがありません。");

                return buffers[index] = data;
            }

            private byte[] ReadUri(string uri)
            {
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = uri.IndexOf(',');

                    if (comma < 0 || !uri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("base64 でない data URI には対応していません。");

                    return Convert.FromBase64String(uri[(comma + 1)..]);
                }

                return File.ReadAllBytes(Path.Combine(directory, Uri.UnescapeDataString(uri)));
            }

            private static int ComponentSize(int type) => type switch
            {
                5120 or 5121 => 1,
                5122 or 5123 => 2,
                5125 or 5126 => 4,
                _ => throw new InvalidDataException($"要素の型 {type} には対応していません。"),
            };

            private static float[] Floats(JsonElement array, int count)
            {
                var values = new float[count];
                var index = 0;

                foreach (var item in array.EnumerateArray())
                {
                    if (index >= count)
                        break;

                    values[index++] = item.GetSingle();
                }

                return values;
            }
        }
    }
}
