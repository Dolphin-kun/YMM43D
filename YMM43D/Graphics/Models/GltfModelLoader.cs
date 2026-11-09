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

            private readonly Dictionary<(int Mesh, int Target), List<ModelMorphDelta>> morphs = [];
            private readonly List<(int Mesh, int Target)> morphOrder = [];

            private readonly Dictionary<int, int> boneOfNode = [];

            private readonly HashSet<int> animated = [];
            private readonly Dictionary<int, int> rigidBoneOfNode = [];
            private readonly List<int> boneOrder = [];
            private readonly Dictionary<int, int> parentOfNode = [];

            private Matrix4x4 baseMatrix = Matrix4x4.Identity;

            public ModelData Read()
            {
                RejectUnsupported();

                baseMatrix = FacesAway() ? Matrix4x4.CreateRotationY(MathF.PI) : Matrix4x4.Identity;
                builder.Root = baseMatrix;

                ReadBones();

                CollectAnimated();

                foreach (var node in RootNodes())
                    Visit(node, baseMatrix, 0);

                FinishMorphs();
                ReadAnimations();
                ReadSprings();

                return builder.Build();
            }

            private void ReadBones()
            {
                if (!root.TryGetProperty("skins", out var skins) || skins.GetArrayLength() == 0)
                    return;

                foreach (var skin in skins.EnumerateArray())
                {
                    if (!skin.TryGetProperty("joints", out var joints))
                        continue;

                    var nodes = joints.EnumerateArray().Select(joint => joint.GetInt32()).ToArray();

                    var inverseBinds = skin.TryGetProperty("inverseBindMatrices", out var accessor)
                        ? ReadMatrices(accessor.GetInt32())
                        : null;

                    var start = builder.Bones.Count;

                    for (var i = 0; i < nodes.Length; i++)
                    {
                        if (boneOfNode.ContainsKey(nodes[i]))
                            continue;

                        boneOfNode[nodes[i]] = builder.Bones.Count;
                        boneOrder.Add(nodes[i]);

                        var node = root.GetProperty("nodes")[nodes[i]];

                        builder.Bones.Add(new ModelBone(
                            node.TryGetProperty("name", out var name) ? name.GetString() ?? $"ボーン {i + 1}" : $"ボーン {i + 1}",
                            -1,
                            LocalMatrix(node),
                            inverseBinds is not null && i < inverseBinds.Length ? inverseBinds[i] : Matrix4x4.Identity));
                    }

                    for (var i = start; i < builder.Bones.Count; i++)
                    {
                        var local = builder.Bones[i].Local;
                        var parent = ParentOfNode(boneOrder[i]);
                        var depth = 0;

                        while (parent >= 0 && !boneOfNode.ContainsKey(parent) && depth++ < 64)
                        {
                            local *= LocalMatrix(root.GetProperty("nodes")[parent]);
                            parent = ParentOfNode(parent);
                        }

                        builder.Bones[i] = builder.Bones[i] with
                        {
                            Local = local,
                            Parent = parent >= 0 && boneOfNode.TryGetValue(parent, out var bone) ? bone : -1,
                        };
                    }
                }
            }

            private int ParentOfNode(int index)
            {
                if (parentOfNode.Count == 0 && root.TryGetProperty("nodes", out var all))
                {
                    var at = 0;

                    foreach (var node in all.EnumerateArray())
                    {
                        if (node.TryGetProperty("children", out var children))
                        {
                            foreach (var child in children.EnumerateArray())
                                parentOfNode[child.GetInt32()] = at;
                        }

                        at++;
                    }
                }

                return parentOfNode.GetValueOrDefault(index, -1);
            }

            private Matrix4x4[] ReadMatrices(int accessorIndex)
            {
                var values = ReadVectors(accessorIndex, 16);
                var matrices = new Matrix4x4[values.Length / 16];

                for (var i = 0; i < matrices.Length; i++)
                {
                    var at = i * 16;

                    matrices[i] = new Matrix4x4(
                        values[at], values[at + 1], values[at + 2], values[at + 3],
                        values[at + 4], values[at + 5], values[at + 6], values[at + 7],
                        values[at + 8], values[at + 9], values[at + 10], values[at + 11],
                        values[at + 12], values[at + 13], values[at + 14], values[at + 15]);
                }

                return matrices;
            }

            private bool FacesAway()
                => root.TryGetProperty("extensions", out var extensions)
                && extensions.TryGetProperty("VRM", out _)
                && !extensions.TryGetProperty("VRMC_vrm", out _);

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
                    var first = root.TryGetProperty("scene", out var scene) ? scene.GetInt32() : 0;

                    var order = Enumerable.Range(0, scenes.GetArrayLength())
                        .OrderBy(index => index == first ? 0 : 1)
                        .ThenBy(index => index);

                    var found = new List<int>();
                    var seen = new HashSet<int>();

                    foreach (var index in order)
                    {
                        if (!scenes[index].TryGetProperty("nodes", out var nodes))
                            continue;

                        foreach (var node in nodes.EnumerateArray())
                        {
                            if (seen.Add(node.GetInt32()))
                                found.Add(node.GetInt32());
                        }
                    }

                    return found;
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

            private void Visit(
                int index, in Matrix4x4 parent, int depth, int moving = -1, Matrix4x4? since = null)
            {
                if (depth > 64)
                    throw new InvalidDataException("ノードの入れ子が深すぎます。");

                var node = root.GetProperty("nodes")[index];
                var local = LocalMatrix(node);
                var world = local * parent;

                var mine = local * (since ?? Matrix4x4.Identity);

                var bone = moving;
                var place = world;
                var below = mine;

                if (animated.Contains(index) && AddMovingBone(index, node, moving, mine, parent) is var made and >= 0)
                {
                    bone = made;
                    place = parent;
                    below = Matrix4x4.Identity;
                }

                if (node.TryGetProperty("mesh", out var mesh))
                {
                    var skinned = node.TryGetProperty("skin", out var skin);

                    var held = skinned || bone < 0
                        ? -1
                        : place == parent ? bone : PlacementBone(bone, mine, world);

                    AddMesh(
                        root.GetProperty("meshes")[mesh.GetInt32()],
                        skinned ? Matrix4x4.Identity : place,
                        skinned ? skin.GetInt32() : -1,
                        mesh.GetInt32(),
                        held);
                }

                if (node.TryGetProperty("children", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                        Visit(child.GetInt32(), world, depth + 1, bone, below);
                }
            }

            private void CollectAnimated()
            {
                if (!root.TryGetProperty("animations", out var animations))
                    return;

                foreach (var animation in animations.EnumerateArray())
                {
                    if (!animation.TryGetProperty("channels", out var channels))
                        continue;

                    foreach (var channel in channels.EnumerateArray())
                    {
                        if (channel.TryGetProperty("target", out var target)
                            && target.TryGetProperty("node", out var node)
                            && target.TryGetProperty("path", out var path)
                            && path.GetString() is "translation" or "rotation" or "scale")
                        {
                            animated.Add(node.GetInt32());
                        }
                    }
                }
            }

            private int AddMovingBone(
                int index, JsonElement node, int parentBone, in Matrix4x4 local, in Matrix4x4 parentWorld)
            {
                if (rigidBoneOfNode.TryGetValue(index, out var known))
                    return known;

                if (boneOfNode.TryGetValue(index, out var already))
                    return rigidBoneOfNode[index] = already;

                if (!Matrix4x4.Invert(parentWorld, out var inverse))
                    return -1;

                builder.Bones.Add(new ModelBone(
                    node.TryGetProperty("name", out var name) ? name.GetString() ?? $"もの {index}" : $"もの {index}",
                    parentBone,
                    local,
                    inverse));

                boneOfNode[index] = builder.Bones.Count - 1;

                return rigidBoneOfNode[index] = builder.Bones.Count - 1;
            }

            private int PlacementBone(int parentBone, in Matrix4x4 local, in Matrix4x4 world)
            {
                if (!Matrix4x4.Invert(world, out var inverse))
                    return -1;

                builder.Bones.Add(new ModelBone("（付いているもの）", parentBone, local, inverse));

                return builder.Bones.Count - 1;
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

            private void AddMesh(
                JsonElement mesh, in Matrix4x4 world, int skinIndex = -1, int meshIndex = -1, int rigidBone = -1)
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

                    var joints = skinIndex >= 0 && attributes.TryGetProperty("JOINTS_0", out var j)
                        ? ReadVectors(j.GetInt32(), 4)
                        : null;

                    var weights = joints is not null && attributes.TryGetProperty("WEIGHTS_0", out var w)
                        ? ReadVectors(w.GetInt32(), 4)
                        : null;

                    var skinJoints = skinIndex >= 0 ? JointsOf(skinIndex) : [];

                    var indices = primitive.TryGetProperty("indices", out var i)
                        ? ReadIndices(i.GetInt32())
                        : [.. Enumerable.Range(0, count).Select(index => (uint)index)];

                    var (color, image, blend, cutoff, unlit, surface) = ReadMaterial(primitive);
                    var (key, name) = MaterialName(primitive);

                    builder.BeginPart(color, image, key, name, blend, cutoff, unlit, surface);

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
                            colors?[v] ?? Vector4.One,
                            rigidBone >= 0
                                ? new ModelInfluence((ushort)rigidBone, 0, 0, 0, new Vector4(1f, 0f, 0f, 0f))
                                : Influence(joints, weights, skinJoints, v));
                    }

                    AddMorphs(mesh, primitive, meshIndex, start, count, normalMatrix);

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

            private void FinishMorphs()
            {
                for (var i = 0; i < morphOrder.Count && i < builder.Morphs.Count; i++)
                    builder.Morphs[i] = builder.Morphs[i] with { Deltas = [.. morphs[morphOrder[i]]] };

                if (ReadVrmExpressions() || ReadVrm1Expressions())
                    return;

                for (var i = 0; i < builder.Morphs.Count; i++)
                {
                    builder.Expressions.Add(
                        new ModelExpression(builder.Morphs[i].Name, string.Empty, [new ModelExpressionBind(i, 1f)]));
                }
            }

            private JsonElement? Extension(string name)
                => root.TryGetProperty("extensions", out var extensions)
                    && extensions.TryGetProperty(name, out var found) ? found : null;

            private bool ReadVrmExpressions()
            {
                if (Extension("VRM") is not { } vrm
                    || !vrm.TryGetProperty("blendShapeMaster", out var master)
                    || !master.TryGetProperty("blendShapeGroups", out var groups))
                {
                    return false;
                }

                foreach (var group in groups.EnumerateArray())
                {
                    var binds = new List<ModelExpressionBind>();

                    if (group.TryGetProperty("binds", out var list))
                    {
                        foreach (var bind in list.EnumerateArray())
                        {
                            var mesh = bind.TryGetProperty("mesh", out var m) ? m.GetInt32() : -1;
                            var index = bind.TryGetProperty("index", out var i) ? i.GetInt32() : -1;
                            var weight = bind.TryGetProperty("weight", out var w) ? w.GetSingle() / 100f : 1f;

                            if (MorphIndex(mesh, index) is { } morph)
                                binds.Add(new ModelExpressionBind(morph, weight));
                        }
                    }

                    var preset = group.TryGetProperty("presetName", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                    var name = group.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;

                    if (binds.Count > 0)
                        builder.Expressions.Add(new ModelExpression(name.Length > 0 ? name : preset, preset, [.. binds]));
                }

                return builder.Expressions.Count > 0;
            }

            private bool ReadVrm1Expressions()
            {
                if (Extension("VRMC_vrm") is not { } vrm || !vrm.TryGetProperty("expressions", out var expressions))
                    return false;

                foreach (var kind in new[] { "preset", "custom" })
                {
                    if (!expressions.TryGetProperty(kind, out var group))
                        continue;

                    foreach (var one in group.EnumerateObject())
                    {
                        var binds = new List<ModelExpressionBind>();

                        if (one.Value.TryGetProperty("morphTargetBinds", out var list))
                        {
                            foreach (var bind in list.EnumerateArray())
                            {
                                var node = bind.TryGetProperty("node", out var n) ? n.GetInt32() : -1;
                                var index = bind.TryGetProperty("index", out var i) ? i.GetInt32() : -1;
                                var weight = bind.TryGetProperty("weight", out var w) ? w.GetSingle() : 1f;

                                if (MorphIndex(MeshOfNode(node), index) is { } morph)
                                    binds.Add(new ModelExpressionBind(morph, weight));
                            }
                        }

                        if (binds.Count > 0)
                            builder.Expressions.Add(new ModelExpression(one.Name, one.Name, [.. binds]));
                    }
                }

                return builder.Expressions.Count > 0;
            }

            private int MeshOfNode(int node)
            {
                if (node < 0 || !root.TryGetProperty("nodes", out var nodes) || node >= nodes.GetArrayLength())
                    return -1;

                return nodes[node].TryGetProperty("mesh", out var mesh) ? mesh.GetInt32() : -1;
            }

            private int? MorphIndex(int mesh, int target)
            {
                var found = morphOrder.IndexOf((mesh, target));

                if (found < 0 && MeshOfNode(mesh) is var byNode and >= 0)
                    found = morphOrder.IndexOf((byNode, target));

                return found >= 0 ? found : null;
            }

            private void AddMorphs(
                JsonElement mesh, JsonElement primitive, int meshIndex, uint start, int count, in Matrix4x4 normalMatrix)
            {
                if (meshIndex < 0 || !primitive.TryGetProperty("targets", out var targets))
                    return;

                var names = TargetNames(mesh, primitive);
                var index = 0;

                foreach (var target in targets.EnumerateArray())
                {
                    if (!target.TryGetProperty("POSITION", out var accessor))
                    {
                        index++;
                        continue;
                    }

                    var offsets = ReadVectors(accessor.GetInt32(), 3);
                    var morph = MorphAt(meshIndex, index, index < names.Length ? names[index] : $"表情 {index + 1}");

                    for (var v = 0; v < count && (v * 3) + 2 < offsets.Length; v++)
                    {
                        var offset = Vector3.TransformNormal(
                            new Vector3(offsets[v * 3], offsets[v * 3 + 1], offsets[v * 3 + 2]), normalMatrix);

                        if (offset.LengthSquared() > ModelBuilder.MorphThreshold * ModelBuilder.MorphThreshold)
                            morph.Add(new ModelMorphDelta(start + (uint)v, offset));
                    }

                    index++;
                }
            }

            private static string[] TargetNames(JsonElement mesh, JsonElement primitive)
            {
                foreach (var holder in new[] { primitive, mesh })
                {
                    if (holder.TryGetProperty("extras", out var extras)
                        && extras.TryGetProperty("targetNames", out var names))
                    {
                        return [.. names.EnumerateArray().Select(one => one.GetString() ?? string.Empty)];
                    }
                }

                return [];
            }

            private List<ModelMorphDelta> MorphAt(int meshIndex, int target, string name)
            {
                if (morphs.TryGetValue((meshIndex, target), out var known))
                    return known;

                morphOrder.Add((meshIndex, target));
                builder.Morphs.Add(new ModelMorph(name, []));

                return morphs[(meshIndex, target)] = [];
            }

            private int[] JointsOf(int skinIndex)
            {
                if (!root.TryGetProperty("skins", out var skins) || skinIndex >= skins.GetArrayLength())
                    return [];

                var skin = skins[skinIndex];

                if (!skin.TryGetProperty("joints", out var joints))
                    return [];

                return [.. joints.EnumerateArray().Select(joint => boneOfNode.GetValueOrDefault(joint.GetInt32(), -1))];
            }

            private static ModelInfluence Influence(float[]? joints, float[]? weights, int[] skinJoints, int vertex)
            {
                if (joints is null || weights is null)
                    return ModelInfluence.None;

                Span<ushort> bones = stackalloc ushort[4];
                Span<float> amounts = stackalloc float[4];

                for (var i = 0; i < 4; i++)
                {
                    var at = vertex * 4 + i;
                    var joint = at < joints.Length ? (int)joints[at] : 0;
                    var bone = joint >= 0 && joint < skinJoints.Length ? skinJoints[joint] : -1;
                    var weight = at < weights.Length ? weights[at] : 0f;

                    bones[i] = bone >= 0 ? (ushort)bone : (ushort)0;
                    amounts[i] = bone >= 0 ? weight : 0f;
                }

                var total = amounts[0] + amounts[1] + amounts[2] + amounts[3];

                if (total <= 1e-6f)
                    return ModelInfluence.None;

                return new ModelInfluence(
                    bones[0], bones[1], bones[2], bones[3],
                    new Vector4(amounts[0], amounts[1], amounts[2], amounts[3]) / total);
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

            private (string Key, string Name) MaterialName(JsonElement primitive)
            {
                if (!primitive.TryGetProperty("material", out var index) || !root.TryGetProperty("materials", out var materials))
                    return (ModelData.NoMaterial, ModelData.NoMaterial);

                var number = index.GetInt32();
                var material = materials[number];

                var name = material.TryGetProperty("name", out var given) && given.GetString() is { Length: > 0 } text
                    ? text
                    : $"材質 {number + 1}";

                return ($"#{number}", name);
            }

            private (Vector4 Color, int Image, ModelBlend Blend, float Cutoff, bool Unlit, ModelSurface Surface)
                ReadMaterial(JsonElement primitive)
            {
                if (!primitive.TryGetProperty("material", out var index) || !root.TryGetProperty("materials", out var materials))
                    return (Vector4.One, -1, ModelBlend.Opaque, 0.5f, false, ModelSurface.Default);

                var number = index.GetInt32();
                var material = materials[number];
                var (blend, cutoff) = ReadAlpha(material);
                var unlit = IsUnlit(material);
                var surface = ReadSurface(number, material);

                if (!material.TryGetProperty("pbrMetallicRoughness", out var pbr))
                    return (Vector4.One, -1, blend, cutoff, unlit, surface);

                var factor = pbr.TryGetProperty("baseColorFactor", out var f) ? Floats(f, 4) : [1f, 1f, 1f, 1f];
                var color = new Vector4(factor[0], factor[1], factor[2], factor[3]);

                if (!pbr.TryGetProperty("baseColorTexture", out var texture))
                    return (color, -1, blend, cutoff, unlit, surface);

                return (
                    color,
                    ImageOfTexture(texture.GetProperty("index").GetInt32()),
                    blend,
                    cutoff,
                    unlit,
                    WithUvTransform(texture, surface));
            }

            private ModelSurface ReadSurface(int number, JsonElement material)
            {
                var surface = ModelSurface.Default;

                if (material.TryGetProperty("normalTexture", out var bump))
                {
                    surface = surface with
                    {
                        BumpImage = ImageOfRef(bump),
                        BumpScale = bump.TryGetProperty("scale", out var scale) ? scale.GetSingle() : 1f,
                    };
                }

                surface = surface with
                {
                    Glow = Triple(material, "emissiveFactor") * GlowStrength(material),
                    GlowImage = material.TryGetProperty("emissiveTexture", out var glow) ? ImageOfRef(glow) : -1,
                };

                if (material.TryGetProperty("pbrMetallicRoughness", out var pbr))
                {
                    surface = surface with
                    {
                        Metallic = pbr.TryGetProperty("metallicFactor", out var metallic) ? metallic.GetSingle() : 1f,
                        Roughness = pbr.TryGetProperty("roughnessFactor", out var rough) ? rough.GetSingle() : 1f,
                        FeelImage = pbr.TryGetProperty("metallicRoughnessTexture", out var feel)
                            ? ImageOfRef(feel)
                            : -1,
                    };
                }

                return ReadToon(number, material, surface);
            }

            private static float GlowStrength(JsonElement material)
                => material.TryGetProperty("extensions", out var extensions)
                    && extensions.TryGetProperty("KHR_materials_emissive_strength", out var found)
                    && found.TryGetProperty("emissiveStrength", out var value)
                        ? value.GetSingle()
                        : 1f;

            private ModelSurface ReadToon(int number, JsonElement material, ModelSurface surface)
            {
                if (material.TryGetProperty("extensions", out var extensions)
                    && extensions.TryGetProperty("VRMC_materials_mtoon", out var mtoon))
                {
                    var shade = mtoon.TryGetProperty("shadeColorFactor", out _)
                        ? Triple(mtoon, "shadeColorFactor")
                        : Vector3.One;

                    return surface with
                    {
                        Shade = shade,
                        ShadeImage = mtoon.TryGetProperty("shadeMultiplyTexture", out var texture)
                            ? ImageOfRef(texture)
                            : -1,
                    };
                }

                if (Extension("VRM") is not { } vrm
                    || !vrm.TryGetProperty("materialProperties", out var list)
                    || number < 0
                    || number >= list.GetArrayLength())
                {
                    return surface;
                }

                var properties = list[number];
                var color = Vector3.One;

                if (properties.TryGetProperty("vectorProperties", out var vectors)
                    && vectors.TryGetProperty("_ShadeColor", out var written)
                    && written.GetArrayLength() >= 3)
                {
                    color = new Vector3(
                        written[0].GetSingle(), written[1].GetSingle(), written[2].GetSingle());
                }

                var image = -1;

                if (properties.TryGetProperty("textureProperties", out var textures)
                    && textures.TryGetProperty("_ShadeTexture", out var shadeTexture))
                {
                    image = ImageOfTexture(shadeTexture.GetInt32());
                }

                return surface with { Shade = color, ShadeImage = image };
            }

            private static ModelSurface WithUvTransform(JsonElement texture, ModelSurface surface)
            {
                if (!texture.TryGetProperty("extensions", out var extensions)
                    || !extensions.TryGetProperty("KHR_texture_transform", out var transform))
                {
                    return surface;
                }

                var scale = transform.TryGetProperty("scale", out var s) ? Floats(s, 2) : [1f, 1f];
                var offset = transform.TryGetProperty("offset", out var o) ? Floats(o, 2) : [0f, 0f];

                return surface with
                {
                    UvScale = new Vector2(scale[0], scale[1]),
                    UvOffset = new Vector2(offset[0], offset[1]),
                    UvRotation = transform.TryGetProperty("rotation", out var r) ? r.GetSingle() : 0f,
                };
            }

            private int ImageOfRef(JsonElement reference)
                => reference.TryGetProperty("index", out var index) ? ImageOfTexture(index.GetInt32()) : -1;

            private static bool IsUnlit(JsonElement material)
                => material.TryGetProperty("extensions", out var extensions)
                    && extensions.TryGetProperty("KHR_materials_unlit", out _);

            private static (ModelBlend Blend, float Cutoff) ReadAlpha(JsonElement material)
            {
                var cutoff = material.TryGetProperty("alphaCutoff", out var given) ? given.GetSingle() : 0.5f;

                var blend = material.TryGetProperty("alphaMode", out var mode) ? mode.GetString() switch
                {
                    "MASK" => ModelBlend.Mask,
                    "BLEND" => ModelBlend.Blend,
                    _ => ModelBlend.Opaque,
                } : ModelBlend.Opaque;

                return (blend, cutoff);
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

            private void ReadAnimations()
            {
                if (!root.TryGetProperty("animations", out var animations))
                    return;

                var number = 0;

                foreach (var animation in animations.EnumerateArray())
                {
                    number++;

                    if (!animation.TryGetProperty("channels", out var channels)
                        || !animation.TryGetProperty("samplers", out var samplers))
                    {
                        continue;
                    }

                    var tracks = new List<ModelTrack>();
                    var moved = new Dictionary<int, ModelAnimatedBone>();
                    var duration = 0f;

                    foreach (var channel in channels.EnumerateArray())
                    {
                        if (!channel.TryGetProperty("target", out var target)
                            || !target.TryGetProperty("node", out var nodeAt)
                            || !target.TryGetProperty("path", out var path)
                            || !channel.TryGetProperty("sampler", out var samplerAt))
                        {
                            continue;
                        }

                        var sampler = samplers[samplerAt.GetInt32()];

                        if (!sampler.TryGetProperty("input", out var input)
                            || !sampler.TryGetProperty("output", out var output))
                        {
                            continue;
                        }

                        var times = ReadVectors(input.GetInt32(), 1);

                        if (times.Length == 0)
                            continue;

                        duration = MathF.Max(duration, times[^1]);

                        var shape = sampler.TryGetProperty("interpolation", out var how)
                            ? how.GetString() ?? "LINEAR"
                            : "LINEAR";

                        var node = nodeAt.GetInt32();
                        var kind = path.GetString() switch
                        {
                            "translation" => ModelTrackKind.Translation,
                            "scale" => ModelTrackKind.Scale,
                            "rotation" => ModelTrackKind.Rotation,
                            "weights" => ModelTrackKind.Weight,
                            _ => (ModelTrackKind?)null,
                        };

                        if (kind is not { } what)
                            continue;

                        if (what == ModelTrackKind.Weight)
                        {
                            AddWeightTracks(tracks, node, times, ReadAccessorValues(output.GetInt32()), shape);

                            continue;
                        }

                        if (!boneOfNode.TryGetValue(node, out var bone))
                            continue;

                        var width = what == ModelTrackKind.Rotation ? 4 : 3;
                        var values = Straighten(ReadAccessorValues(output.GetInt32()), times.Length, width, shape);

                        if (values.Length < times.Length * width)
                            continue;

                        if (!moved.ContainsKey(bone) && RestOf(node, bone) is { } rest)
                            moved[bone] = rest;

                        if (moved.ContainsKey(bone))
                            tracks.Add(new ModelTrack(bone, -1, what, times, values, shape == "STEP"));
                    }

                    if (tracks.Count == 0)
                        continue;

                    var name = animation.TryGetProperty("name", out var given) && given.GetString() is { Length: > 0 } text
                        ? text
                        : $"動き {number}";

                    builder.Animations.Add(new ModelAnimation(name, duration, [.. tracks], [.. moved.Values]));
                }
            }

            private void AddWeightTracks(
                List<ModelTrack> tracks, int node, float[] times, float[] values, string shape)
            {
                var mesh = MeshOfNode(node);

                if (mesh < 0)
                    return;

                var straight = Straighten(values, times.Length, values.Length / Math.Max(times.Length, 1), shape);
                var width = straight.Length / Math.Max(times.Length, 1);

                for (var target = 0; target < width; target++)
                {
                    if (MorphIndex(mesh, target) is not { } morph)
                        continue;

                    var one = new float[times.Length];

                    for (var i = 0; i < times.Length; i++)
                        one[i] = straight[i * width + target];

                    tracks.Add(new ModelTrack(-1, morph, ModelTrackKind.Weight, times, one, shape == "STEP"));
                }
            }

            private static float[] Straighten(float[] values, int count, int width, string shape)
            {
                if (shape != "CUBICSPLINE" || count <= 0 || width <= 0)
                    return values;

                var each = width / 3;

                if (each <= 0 || values.Length < count * width)
                    return values;

                var made = new float[count * each];

                for (var i = 0; i < count; i++)
                {
                    for (var k = 0; k < each; k++)
                        made[i * each + k] = values[i * width + each + k];
                }

                return made;
            }

            private ModelAnimatedBone? RestOf(int node, int bone)
            {
                var found = root.GetProperty("nodes")[node];

                if (!Matrix4x4.Invert(LocalMatrix(found), out var inverse))
                    return null;

                var scale = found.TryGetProperty("scale", out var s) ? Floats(s, 3) : [1f, 1f, 1f];
                var turn = found.TryGetProperty("rotation", out var r) ? Floats(r, 4) : [0f, 0f, 0f, 1f];
                var shift = found.TryGetProperty("translation", out var t) ? Floats(t, 3) : [0f, 0f, 0f];

                return new ModelAnimatedBone(
                    bone,
                    new Vector3(shift[0], shift[1], shift[2]),
                    Quaternion.Normalize(new Quaternion(turn[0], turn[1], turn[2], turn[3])),
                    new Vector3(scale[0], scale[1], scale[2]),
                    inverse);
            }

            private float[] ReadAccessorValues(int accessorIndex)
                => ReadAccessor(root.GetProperty("accessors")[accessorIndex], out _);

            private void ReadSprings()
            {
                if (builder.Bones.Count == 0)
                    return;

                if (!ReadVrm1Springs())
                    ReadVrmSprings();
            }

            private void ReadVrmSprings()
            {
                if (Extension("VRM") is not { } vrm
                    || !vrm.TryGetProperty("secondaryAnimation", out var secondary)
                    || !secondary.TryGetProperty("boneGroups", out var groups))
                {
                    return;
                }

                var hits = ReadVrmColliderGroups(secondary);

                foreach (var group in groups.EnumerateArray())
                {
                    if (!group.TryGetProperty("bones", out var bones))
                        continue;

                    var stiffness = Number(group, "stiffiness", 1f);
                    var drag = Number(group, "dragForce", 0.4f);
                    var power = Number(group, "gravityPower", 0f);
                    var direction = group.TryGetProperty("gravityDir", out var dir)
                        ? new Vector3(Number(dir, "x", 0f), Number(dir, "y", -1f), Number(dir, "z", 0f))
                        : new Vector3(0f, -1f, 0f);

                    var thickness = Number(group, "hitRadius", 0f);
                    var touching = Gather(hits, group, "colliderGroups");

                    foreach (var node in bones.EnumerateArray())
                    {
                        if (boneOfNode.TryGetValue(node.GetInt32(), out var bone))
                            AddSpring(bone, stiffness, drag, power * direction, thickness, touching);
                    }
                }
            }

            private List<ModelSpringCollider[]> ReadVrmColliderGroups(JsonElement secondary)
            {
                var groups = new List<ModelSpringCollider[]>();

                if (!secondary.TryGetProperty("colliderGroups", out var found))
                    return groups;

                foreach (var group in found.EnumerateArray())
                {
                    var made = new List<ModelSpringCollider>();

                    if (group.TryGetProperty("node", out var node)
                        && boneOfNode.TryGetValue(node.GetInt32(), out var bone)
                        && group.TryGetProperty("colliders", out var shapes))
                    {
                        foreach (var shape in shapes.EnumerateArray())
                        {
                            var offset = shape.TryGetProperty("offset", out var at)
                                ? new Vector3(Number(at, "x", 0f), Number(at, "y", 0f), Number(at, "z", 0f))
                                : Vector3.Zero;

                            var radius = Number(shape, "radius", 0f);

                            if (radius > 0f)
                                made.Add(new ModelSpringCollider(bone, offset, radius, Vector3.Zero, false));
                        }
                    }

                    groups.Add([.. made]);
                    builder.Colliders.AddRange(made);
                }

                return groups;
            }

            private static ModelSpringCollider[] Gather(
                List<ModelSpringCollider[]> groups, JsonElement owner, string name)
            {
                if (!owner.TryGetProperty(name, out var chosen))
                    return [];

                var made = new List<ModelSpringCollider>();

                foreach (var index in chosen.EnumerateArray())
                {
                    if (index.TryGetInt32(out var at) && at >= 0 && at < groups.Count)
                        made.AddRange(groups[at]);
                }

                return [.. made];
            }

            private bool ReadVrm1Springs()
            {
                if (Extension("VRMC_springBone") is not { } spring
                    || !spring.TryGetProperty("springs", out var springs))
                {
                    return false;
                }

                var found = false;
                var hits = ReadVrm1ColliderGroups(spring);

                foreach (var one in springs.EnumerateArray())
                {
                    if (!one.TryGetProperty("joints", out var joints))
                        continue;

                    var made = new List<ModelSpringJoint>();

                    foreach (var joint in joints.EnumerateArray())
                    {
                        if (!joint.TryGetProperty("node", out var node)
                            || !boneOfNode.TryGetValue(node.GetInt32(), out var bone))
                        {
                            continue;
                        }

                        var power = Number(joint, "gravityPower", 0f);
                        var direction = joint.TryGetProperty("gravityDir", out var dir)
                            ? new Vector3(Number(dir, "x", 0f), Number(dir, "y", -1f), Number(dir, "z", 0f))
                            : new Vector3(0f, -1f, 0f);

                        if (Reach(bone) is not { } reach)
                            continue;

                        made.Add(new ModelSpringJoint(
                            bone,
                            reach.Axis,
                            reach.Length,
                            Number(joint, "stiffness", 1f),
                            Number(joint, "dragForce", 0.4f),
                            power * direction,
                            Number(joint, "hitRadius", 0f)));
                    }

                    if (made.Count == 0)
                        continue;

                    builder.Springs.Add(ModelSpring.Allowing(
                        new ModelSpring([.. made]) { Colliders = Gather(hits, one, "colliderGroups") },
                        [.. builder.Bones]));

                    found = true;
                }

                return found;
            }

            private List<ModelSpringCollider[]> ReadVrm1ColliderGroups(JsonElement spring)
            {
                var all = new List<ModelSpringCollider>();

                if (spring.TryGetProperty("colliders", out var colliders))
                {
                    foreach (var collider in colliders.EnumerateArray())
                    {
                        all.Add(ReadVrm1Collider(collider));
                        builder.Colliders.Add(all[^1]);
                    }
                }

                var groups = new List<ModelSpringCollider[]>();

                if (!spring.TryGetProperty("colliderGroups", out var found))
                    return groups;

                foreach (var group in found.EnumerateArray())
                {
                    var made = new List<ModelSpringCollider>();

                    if (group.TryGetProperty("colliders", out var chosen))
                    {
                        foreach (var index in chosen.EnumerateArray())
                        {
                            if (index.TryGetInt32(out var at) && at >= 0 && at < all.Count
                                && all[at].Radius > 0f)
                            {
                                made.Add(all[at]);
                            }
                        }
                    }

                    groups.Add([.. made]);
                }

                return groups;
            }

            private ModelSpringCollider ReadVrm1Collider(JsonElement collider)
            {
                var bone = collider.TryGetProperty("node", out var node)
                    && boneOfNode.TryGetValue(node.GetInt32(), out var found)
                        ? found
                        : -1;

                if (bone < 0 || !collider.TryGetProperty("shape", out var shape))
                    return new ModelSpringCollider(-1, Vector3.Zero, 0f, Vector3.Zero, false);

                if (shape.TryGetProperty("capsule", out var capsule))
                {
                    return new ModelSpringCollider(
                        bone,
                        Triple(capsule, "offset"),
                        Number(capsule, "radius", 0f),
                        Triple(capsule, "tail"),
                        true);
                }

                if (shape.TryGetProperty("sphere", out var sphere))
                {
                    return new ModelSpringCollider(
                        bone,
                        Triple(sphere, "offset"),
                        Number(sphere, "radius", 0f),
                        Vector3.Zero,
                        false);
                }

                return new ModelSpringCollider(-1, Vector3.Zero, 0f, Vector3.Zero, false);
            }

            private static Vector3 Triple(JsonElement owner, string name)
            {
                if (!owner.TryGetProperty(name, out var found) || found.GetArrayLength() < 3)
                    return Vector3.Zero;

                return new Vector3(
                    found[0].GetSingle(), found[1].GetSingle(), found[2].GetSingle());
            }

            private void AddSpring(
                int root,
                float stiffness,
                float drag,
                in Vector3 gravity,
                float hitRadius,
                ModelSpringCollider[] colliders)
            {
                if (ModelSpring.From(
                        [.. builder.Bones], root, stiffness, drag, gravity, hitRadius, colliders) is { } made)
                {
                    builder.Springs.Add(made);
                }
            }

            private (Vector3 Axis, float Length)? Reach(int bone)
                => ModelSpring.Reach([.. builder.Bones], bone);

            private static float Number(JsonElement element, string name, float fallback)
                => element.TryGetProperty(name, out var found) && found.TryGetSingle(out var value)
                    ? value
                    : fallback;

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
                    "MAT2" => 4,
                    "MAT3" => 9,
                    "MAT4" => 16,
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
