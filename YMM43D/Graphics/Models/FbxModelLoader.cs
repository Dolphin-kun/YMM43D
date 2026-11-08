using System.IO;
using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public static class FbxModelLoader
    {
        public static ModelData Load(string path)
        {
            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full) ?? string.Empty;

            return new Reader(FbxDocument.Load(full), directory).Read();
        }

        private sealed class Reader(FbxNode root, string directory)
        {
            private readonly ModelBuilder builder = new();

            private readonly Dictionary<long, FbxNode> objects = [];
            private readonly Dictionary<long, List<long>> childrenOf = [];
            private readonly Dictionary<long, List<(long Child, string Property)>> propertiesOf = [];
            private readonly Dictionary<long, List<long>> parentsOf = [];
            private readonly Dictionary<long, int> images = [];
            private readonly Dictionary<(long Material, string Slot), int> slotImages = [];

            private UnityPackage? unity;
            private bool searchedUnity;
            private readonly Dictionary<long, Matrix4x4> worlds = [];
            private readonly Dictionary<(long Skin, long Model), int> boneOfModel = [];
            private readonly List<(long Skin, long Model)> boneModels = [];

            private readonly Dictionary<string, List<ModelMorphDelta>> shapes = [];
            private readonly List<string> shapeOrder = [];
            private HashSet<string>? shownShapes;

            public ModelData Read()
            {
                Collect();

                var axis = AxisMatrix();
                builder.Root = axis;

                foreach (var (id, node) in objects)
                {
                    if (node.Name != "Model")
                        continue;

                    foreach (var child in childrenOf.GetValueOrDefault(id) ?? [])
                    {
                        if (objects.GetValueOrDefault(child) is { Name: "Geometry" } geometry)
                            AddGeometry(geometry, id, GeometricMatrix(node) * WorldOf(id) * axis, GeometricMatrix(node));
                    }
                }

                LinkBones();
                FinishShapes();

                return builder.Build();
            }

            private void LinkBones()
            {
                for (var i = 0; i < boneModels.Count; i++)
                {
                    var (skin, model) = boneModels[i];
                    var local = LocalMatrix(objects[model]);
                    var parent = ParentModelOf(model);
                    var depth = 0;

                    while (parent != 0 && !boneOfModel.ContainsKey((skin, parent)) && depth++ < 64)
                    {
                        local *= LocalMatrix(objects[parent]);
                        parent = ParentModelOf(parent);
                    }

                    builder.Bones[i] = builder.Bones[i] with
                    {
                        Local = local,
                        Parent = parent != 0 && boneOfModel.TryGetValue((skin, parent), out var bone) ? bone : -1,
                    };
                }
            }

            private void Collect()
            {
                if (root.Child("Objects") is not { } all || root.Child("Connections") is not { } connections)
                    throw new InvalidDataException("fbx に物のつながりがありません。新しい形式で書き出し直してください。");

                foreach (var node in all.Children)
                    objects[node.Number(0)] = node;

                foreach (var link in connections.ChildrenNamed("C"))
                {
                    var kind = link.Text(0);
                    var child = link.Number(1);
                    var parent = link.Number(2);

                    if (kind == "OO")
                    {
                        (childrenOf.TryGetValue(parent, out var list) ? list : childrenOf[parent] = []).Add(child);
                        (parentsOf.TryGetValue(child, out var parents) ? parents : parentsOf[child] = []).Add(parent);
                    }
                    else if (kind == "OP")
                    {
                        (propertiesOf.TryGetValue(parent, out var list) ? list : propertiesOf[parent] = [])
                            .Add((child, link.Text(3)));
                    }
                }
            }

            private Matrix4x4 AxisMatrix()
            {
                if (root.Child("GlobalSettings")?.Child("Properties70") is not { } settings)
                    return Matrix4x4.Identity;

                var up = (int)Value(settings, "UpAxis", 1);
                var upward = Value(settings, "UpAxisSign", 1) < 0;

                return (up, upward) switch
                {
                    (0, false) => new Matrix4x4(0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f),
                    (0, true) => new Matrix4x4(0f, -1f, 0f, 0f, 0f, 0f, 1f, 0f, -1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f),

                    (2, false) => new Matrix4x4(1f, 0f, 0f, 0f, 0f, 0f, -1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f),
                    (2, true) => new Matrix4x4(1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, -1f, 0f, 0f, 0f, 0f, 0f, 1f),

                    (_, true) => new Matrix4x4(1f, 0f, 0f, 0f, 0f, -1f, 0f, 0f, 0f, 0f, -1f, 0f, 0f, 0f, 0f, 1f),
                    _ => Matrix4x4.Identity,
                };
            }

            private long ParentModelOf(long id)
            {
                foreach (var parent in parentsOf.GetValueOrDefault(id) ?? [])
                {
                    if (objects.GetValueOrDefault(parent) is { Name: "Model" })
                        return parent;
                }

                return 0;
            }

            private Matrix4x4 WorldOf(long id)
            {
                if (worlds.TryGetValue(id, out var known))
                    return known;

                worlds[id] = Matrix4x4.Identity;

                var node = objects.GetValueOrDefault(id);
                var local = node is null ? Matrix4x4.Identity : LocalMatrix(node);

                var parent = ParentModelOf(id);
                var world = parent != 0 ? local * WorldOf(parent) : local;

                return worlds[id] = world;
            }

            private static Matrix4x4 LocalMatrix(FbxNode node)
            {
                if (node.Child("Properties70") is not { } properties)
                    return Matrix4x4.Identity;

                var order = (int)Value(properties, "RotationOrder", 0);

                var translation = Vector(properties, "Lcl Translation", Vector3.Zero);
                var rotation = Euler(Vector(properties, "Lcl Rotation", Vector3.Zero), order);
                var scale = Vector(properties, "Lcl Scaling", Vector3.One);

                var preRotation = Euler(Vector(properties, "PreRotation", Vector3.Zero), order);
                var postRotation = Euler(Vector(properties, "PostRotation", Vector3.Zero), order);
                Matrix4x4.Invert(postRotation, out var postInverse);

                var rotationOffset = Vector(properties, "RotationOffset", Vector3.Zero);
                var rotationPivot = Vector(properties, "RotationPivot", Vector3.Zero);
                var scalingOffset = Vector(properties, "ScalingOffset", Vector3.Zero);
                var scalingPivot = Vector(properties, "ScalingPivot", Vector3.Zero);

                return Matrix4x4.CreateTranslation(-scalingPivot)
                     * Matrix4x4.CreateScale(scale)
                     * Matrix4x4.CreateTranslation(scalingPivot)
                     * Matrix4x4.CreateTranslation(scalingOffset)
                     * Matrix4x4.CreateTranslation(-rotationPivot)
                     * postInverse
                     * rotation
                     * preRotation
                     * Matrix4x4.CreateTranslation(rotationPivot)
                     * Matrix4x4.CreateTranslation(rotationOffset)
                     * Matrix4x4.CreateTranslation(translation);
            }

            private static Matrix4x4 GeometricMatrix(FbxNode node)
            {
                if (node.Child("Properties70") is not { } properties)
                    return Matrix4x4.Identity;

                var translation = Vector(properties, "GeometricTranslation", Vector3.Zero);
                var rotation = Vector(properties, "GeometricRotation", Vector3.Zero);
                var scale = Vector(properties, "GeometricScaling", Vector3.One);

                return Matrix4x4.CreateScale(scale)
                     * Euler(rotation, (int)Value(properties, "RotationOrder", 0))
                     * Matrix4x4.CreateTranslation(translation);
            }

            private static Matrix4x4 Euler(Vector3 degrees, int order)
            {
                var x = Matrix4x4.CreateRotationX(float.DegreesToRadians(degrees.X));
                var y = Matrix4x4.CreateRotationY(float.DegreesToRadians(degrees.Y));
                var z = Matrix4x4.CreateRotationZ(float.DegreesToRadians(degrees.Z));

                return order switch
                {
                    1 => x * z * y,
                    2 => y * z * x,
                    3 => y * x * z,
                    4 => z * x * y,
                    5 => z * y * x,
                    _ => x * y * z,
                };
            }

            private static Vector3 Vector(FbxNode properties, string name, Vector3 fallback)
            {
                if (Find(properties, name) is not { } property || property.Properties.Length < 7)
                    return fallback;

                return new Vector3(
                    (float)Double(property, 4), (float)Double(property, 5), (float)Double(property, 6));
            }

            private static double Value(FbxNode properties, string name, double fallback)
                => Find(properties, name) is { } property && property.Properties.Length > 4
                    ? Double(property, 4)
                    : fallback;

            private static FbxNode? Find(FbxNode properties, string name)
            {
                foreach (var property in properties.ChildrenNamed("P"))
                {
                    if (property.Text(0) == name)
                        return property;
                }

                return null;
            }

            private static double Double(FbxNode node, int index)
                => index < node.Properties.Length && node.Properties[index] is { } value
                    ? value switch
                    {
                        double number => number,
                        float number => number,
                        int number => number,
                        long number => number,
                        short number => number,
                        _ => 0d,
                    }
                    : 0d;

            private void AddGeometry(FbxNode geometry, long modelId, in Matrix4x4 world, Matrix4x4 geometric)
            {
                var positions = geometry.DoublesOf("Vertices");
                var polygons = geometry.IntegersOf("PolygonVertexIndex");

                if (positions.Length < 3 || polygons.Length < 3)
                    return;

                var pointCount = positions.Length / 3;

                var influences = ReadSkin(geometry, pointCount, geometric);
                var placed = influences is null ? world : Matrix4x4.Identity;

                Matrix4x4.Invert(placed, out var inverse);
                var normalMatrix = Matrix4x4.Transpose(inverse);

                var normals = Layer(geometry, "LayerElementNormal", "Normals", "NormalsIndex");
                var texCoords = Layer(geometry, "LayerElementUV", "UV", "UVIndex");
                var materials = MaterialLayer(geometry);
                var slots = MaterialSlots(modelId);

                var corners = new List<(int Point, int Corner)>();
                var groups = new Dictionary<int, List<(int Point, int Corner)[]>>();
                var polygon = 0;

                for (var i = 0; i < polygons.Length; i++)
                {
                    var raw = polygons[i];
                    var point = raw < 0 ? ~raw : raw;

                    if (point < 0 || point >= pointCount)
                        throw new InvalidDataException("fbx の頂点番号が頂点の数を越えています。");

                    corners.Add((point, i));

                    if (raw >= 0)
                        continue;

                    var slot = materials.At(polygon, polygon);
                    var triangles = groups.TryGetValue(slot, out var known) ? known : groups[slot] = [];

                    for (var t = 1; t + 1 < corners.Count; t++)
                        triangles.Add([corners[0], corners[t], corners[t + 1]]);

                    corners.Clear();
                    polygon++;
                }

                var madeFrom = new Dictionary<int, List<uint>>();

                foreach (var (slot, triangles) in groups)
                {
                    var (color, image, key, name) = Material(slots, slot);

                    builder.BeginPart(color, image, key, name, surface: Surface(slots, slot));

                    var known = new Dictionary<(int Point, int Normal, int TexCoord), uint>();

                    foreach (var triangle in triangles)
                    {
                        var made = new uint[3];

                        for (var c = 0; c < 3; c++)
                        {
                            var (point, corner) = triangle[c];

                            var normalAt = normals.IndexOf(corner, point);
                            var texCoordAt = texCoords.IndexOf(corner, point);
                            var vertexKey = (point, normalAt, texCoordAt);

                            if (!known.TryGetValue(vertexKey, out made[c]))
                            {
                                var position = Vector3.Transform(
                                    new Vector3(
                                        (float)positions[point * 3],
                                        (float)positions[point * 3 + 1],
                                        (float)positions[point * 3 + 2]),
                                    placed);

                                var normal = normals.Vector3At(normalAt);
                                normal = Vector3.TransformNormal(normal, normalMatrix);

                                var uv = texCoords.Vector2At(texCoordAt);

                                made[c] = known[vertexKey] = builder.AddVertex(
                                    position,
                                    normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.Zero,
                                    new Vector2(uv.X, 1f - uv.Y),
                                    Vector4.One,
                                    influences is not null && point < influences.Length
                                        ? influences[point]
                                        : ModelInfluence.None);

                                (madeFrom.TryGetValue(point, out var same) ? same : madeFrom[point] = [])
                                    .Add(made[c]);
                            }
                        }

                        builder.AddTriangle(made[0], made[1], made[2]);

                        if (!normals.HasValues)
                            FillFlatNormal(made[0], made[1], made[2]);
                    }
                }

                ReadShapes(geometry, madeFrom, placed);
            }

            private void ReadShapes(FbxNode geometry, Dictionary<int, List<uint>> madeFrom, in Matrix4x4 placed)
            {
                Matrix4x4.Invert(placed, out var inverse);
                var direction = Matrix4x4.Transpose(inverse);

                foreach (var blendId in childrenOf.GetValueOrDefault(geometry.Number(0)) ?? [])
                {
                    if (objects.GetValueOrDefault(blendId) is not { Name: "Deformer" } blend
                        || Subclass(blend) != "BlendShape")
                    {
                        continue;
                    }

                    foreach (var channelId in childrenOf.GetValueOrDefault(blendId) ?? [])
                    {
                        if (objects.GetValueOrDefault(channelId) is not { Name: "Deformer" } channel
                            || Subclass(channel) != "BlendShapeChannel"
                            || !Shown(NameOf(channel)))
                        {
                            continue;
                        }

                        foreach (var shapeId in childrenOf.GetValueOrDefault(channelId) ?? [])
                        {
                            if (objects.GetValueOrDefault(shapeId) is { Name: "Geometry" } shape
                                && Subclass(shape) == "Shape")
                            {
                                AddShape(NameOf(channel), shape, madeFrom, direction);
                            }
                        }
                    }
                }
            }

            private bool Shown(string name)
            {
                if (shownShapes is null)
                {
                    var all = new List<string>();

                    foreach (var (id, node) in objects)
                    {
                        if (node.Name == "Deformer" && Subclass(node) == "BlendShapeChannel")
                            all.Add(NameOf(node));
                    }

                    var useful = all.Where(one => !IsDivider(one)).ToList();
                    var shallow = useful.Count > 0 ? useful.Min(Depth) : 0;

                    shownShapes = [.. useful.Where(one => Depth(one) == shallow)];
                }

                return shownShapes.Contains(name);
            }

            private static bool IsDivider(string name)
                => name.Length > 2 && name.StartsWith('-') && name.EndsWith('-');

            private static int Depth(string name) => name.Count(letter => letter is '.' or '_');

            private void AddShape(
                string name, FbxNode shape, Dictionary<int, List<uint>> madeFrom, in Matrix4x4 direction)
            {
                var points = shape.IntegersOf("Indexes");
                var offsets = shape.DoublesOf("Vertices");

                if (points.Length == 0 || offsets.Length < points.Length * 3)
                    return;

                if (!shapes.TryGetValue(name, out var deltas))
                {
                    shapes[name] = deltas = [];
                    shapeOrder.Add(name);
                }

                for (var i = 0; i < points.Length; i++)
                {
                    if (!madeFrom.TryGetValue(points[i], out var vertices))
                        continue;

                    var offset = Vector3.TransformNormal(
                        new Vector3(
                            (float)offsets[i * 3], (float)offsets[i * 3 + 1], (float)offsets[i * 3 + 2]),
                        direction);

                    if (offset.LengthSquared() < ModelBuilder.MorphThreshold * ModelBuilder.MorphThreshold)
                        continue;

                    foreach (var vertex in vertices)
                        deltas.Add(new ModelMorphDelta(vertex, offset));
                }
            }

            private void FinishShapes()
            {
                foreach (var name in shapeOrder)
                {
                    if (shapes[name].Count == 0)
                        continue;

                    builder.Expressions.Add(
                        new ModelExpression(name, string.Empty, [new ModelExpressionBind(builder.Morphs.Count, 1f)]));

                    builder.Morphs.Add(new ModelMorph(name, [.. shapes[name]]));
                }
            }

            private ModelInfluence[]? ReadSkin(FbxNode geometry, int pointCount, Matrix4x4 geometric)
            {
                List<(int Bone, float Weight)>[]? lists = null;

                foreach (var skinId in childrenOf.GetValueOrDefault(geometry.Number(0)) ?? [])
                {
                    if (objects.GetValueOrDefault(skinId) is not { Name: "Deformer" } skin || Subclass(skin) != "Skin")
                        continue;

                    foreach (var clusterId in childrenOf.GetValueOrDefault(skinId) ?? [])
                    {
                        if (objects.GetValueOrDefault(clusterId) is not { Name: "Deformer" } cluster
                            || Subclass(cluster) != "Cluster")
                        {
                            continue;
                        }

                        var indexes = cluster.IntegersOf("Indexes");
                        var weights = cluster.DoublesOf("Weights");

                        if (indexes.Length == 0 || BoneOf(cluster, clusterId, skinId, geometric) is not { } bone)
                            continue;

                        lists ??= new List<(int, float)>[pointCount];

                        for (var i = 0; i < indexes.Length && i < weights.Length; i++)
                        {
                            var point = indexes[i];

                            if (point < 0 || point >= pointCount || weights[i] <= 0d)
                                continue;

                            (lists[point] ??= []).Add((bone, (float)weights[i]));
                        }
                    }
                }

                if (lists is null)
                    return null;

                var influences = new ModelInfluence[pointCount];

                for (var i = 0; i < pointCount; i++)
                    influences[i] = Pick(lists[i]);

                return influences;
            }

            private static ModelInfluence Pick(List<(int Bone, float Weight)>? list)
            {
                if (list is null || list.Count == 0)
                    return ModelInfluence.None;

                list.Sort((a, b) => b.Weight.CompareTo(a.Weight));

                Span<ushort> bones = stackalloc ushort[4];
                Span<float> amounts = stackalloc float[4];
                var total = 0f;

                for (var i = 0; i < 4 && i < list.Count; i++)
                {
                    bones[i] = (ushort)list[i].Bone;
                    amounts[i] = list[i].Weight;
                    total += list[i].Weight;
                }

                if (total <= 1e-6f)
                    return ModelInfluence.None;

                return new ModelInfluence(
                    bones[0], bones[1], bones[2], bones[3],
                    new Vector4(amounts[0], amounts[1], amounts[2], amounts[3]) / total);
            }

            private int? BoneOf(FbxNode cluster, long clusterId, long skinId, in Matrix4x4 geometric)
            {
                long model = 0;

                foreach (var child in childrenOf.GetValueOrDefault(clusterId) ?? [])
                {
                    if (objects.GetValueOrDefault(child) is { Name: "Model" })
                    {
                        model = child;
                        break;
                    }
                }

                if (model == 0 || objects.GetValueOrDefault(model) is not { } node)
                    return null;

                if (boneOfModel.TryGetValue((skinId, model), out var known))
                    return known;

                var bind = geometric * Matrix(cluster.DoublesOf("Transform"));

                boneOfModel[(skinId, model)] = builder.Bones.Count;
                boneModels.Add((skinId, model));

                builder.Bones.Add(new ModelBone(NameOf(node), -1, LocalMatrix(node), bind));

                return boneOfModel[(skinId, model)];
            }

            private static Matrix4x4 Matrix(double[] values)
            {
                if (values.Length < 16)
                    return Matrix4x4.Identity;

                return new Matrix4x4(
                    (float)values[0], (float)values[1], (float)values[2], (float)values[3],
                    (float)values[4], (float)values[5], (float)values[6], (float)values[7],
                    (float)values[8], (float)values[9], (float)values[10], (float)values[11],
                    (float)values[12], (float)values[13], (float)values[14], (float)values[15]);
            }

            private static string Subclass(FbxNode node)
            {
                var text = node.Text(2);
                var end = text.IndexOf('\0');

                return end >= 0 ? text[..end] : text;
            }

            private void FillFlatNormal(uint a, uint b, uint c)
            {
                var normal = Vector3.Cross(
                    builder.PositionOf(b) - builder.PositionOf(a),
                    builder.PositionOf(c) - builder.PositionOf(a));

                if (normal.LengthSquared() <= 1e-20f)
                    return;

                normal = Vector3.Normalize(normal);

                builder.SetNormal(a, normal);
                builder.SetNormal(b, normal);
                builder.SetNormal(c, normal);
            }

            private readonly record struct LayerValues(double[] Values, int[] Indices, bool PerPoint, int Width)
            {
                public bool HasValues => Values.Length > 0;

                public int IndexOf(int corner, int point)
                {
                    if (!HasValues)
                        return -1;

                    var at = PerPoint ? point : corner;

                    return Indices.Length > 0 ? (at < Indices.Length ? Indices[at] : -1) : at;
                }

                public Vector3 Vector3At(int index)
                {
                    var at = index * Width;

                    return index < 0 || at + 2 >= Values.Length
                        ? Vector3.Zero
                        : new Vector3((float)Values[at], (float)Values[at + 1], (float)Values[at + 2]);
                }

                public Vector2 Vector2At(int index)
                {
                    var at = index * Width;

                    return index < 0 || at + 1 >= Values.Length
                        ? Vector2.Zero
                        : new Vector2((float)Values[at], (float)Values[at + 1]);
                }
            }

            private readonly record struct MaterialValues(int[] Values, bool AllSame)
            {
                public int At(int polygon, int fallback)
                {
                    if (Values.Length == 0)
                        return 0;

                    if (AllSame)
                        return Values[0];

                    return polygon < Values.Length ? Values[polygon] : (fallback < Values.Length ? Values[fallback] : 0);
                }
            }

            private static LayerValues Layer(FbxNode geometry, string layerName, string valueName, string indexName)
            {
                if (geometry.Child(layerName) is not { } layer)
                    return new LayerValues([], [], false, 3);

                var mapping = layer.TextOf("MappingInformationType");
                var reference = layer.TextOf("ReferenceInformationType");

                var values = layer.DoublesOf(valueName);
                var indices = reference.StartsWith("IndexToDirect", StringComparison.Ordinal)
                    ? layer.IntegersOf(indexName)
                    : [];

                var width = valueName == "UV" ? 2 : 3;
                var perPoint = mapping is "ByVertice" or "ByVertex" or "ByControlPoint";

                return new LayerValues(values, indices, perPoint, width);
            }

            private static MaterialValues MaterialLayer(FbxNode geometry)
            {
                if (geometry.Child("LayerElementMaterial") is not { } layer)
                    return new MaterialValues([], true);

                return new MaterialValues(
                    layer.IntegersOf("Materials"),
                    layer.TextOf("MappingInformationType") == "AllSame");
            }

            private List<long> MaterialSlots(long modelId)
            {
                var slots = new List<long>();

                foreach (var child in childrenOf.GetValueOrDefault(modelId) ?? [])
                {
                    if (objects.GetValueOrDefault(child) is { Name: "Material" })
                        slots.Add(child);
                }

                return slots;
            }

            private (Vector4 Color, int Image, string Key, string Name) Material(List<long> slots, int slot)
            {
                if (slot < 0 || slot >= slots.Count || objects.GetValueOrDefault(slots[slot]) is not { } material)
                    return (Vector4.One, -1, ModelData.NoMaterial, ModelData.NoMaterial);

                var id = slots[slot];
                var image = ImageOf(id);
                var color = Vector4.One;

                if (image < 0 && material.Child("Properties70") is { } properties)
                {
                    var diffuse = Vector(properties, "DiffuseColor", Vector(properties, "Diffuse", Vector3.One));
                    color = new Vector4(diffuse, 1f);
                }

                return (color, image, $"#{id}", NameOf(material));
            }

            private ModelSurface Surface(List<long> slots, int slot)
            {
                if (slot < 0 || slot >= slots.Count
                    || objects.GetValueOrDefault(slots[slot]) is not { } material)
                {
                    return ModelSurface.Default;
                }

                var id = slots[slot];
                var surface = ModelSurface.Default;
                var bump = ImageOfSlot(id, "NormalMap", "Bump");

                if (bump >= 0)
                    surface = surface with { BumpImage = bump };

                var glowImage = ImageOfSlot(id, "EmissiveColor", "Emissive");
                var glow = Vector3.Zero;

                if (material.Child("Properties70") is { } properties)
                {
                    glow = Vector(properties, "EmissiveColor", Vector(properties, "Emissive", Vector3.Zero))
                         * (float)Value(properties, "EmissiveFactor", 1.0);
                }

                if (glowImage >= 0 && glow == Vector3.Zero)
                    glow = Vector3.One;

                return surface with { Glow = glow, GlowImage = glowImage };
            }

            private static string NameOf(FbxNode node)
            {
                var text = node.Text(1);
                var end = text.IndexOf('\0');
                var name = end >= 0 ? text[..end] : text;

                return name.Length > 0 ? name : ModelData.NoMaterial;
            }

            private int ImageOf(long materialId)
            {
                if (images.TryGetValue(materialId, out var known))
                    return known;

                images[materialId] = -1;

                try
                {
                    if (FindTexture(materialId) is { } texture && ImageBytes(texture) is { Length: > 0 } data)
                        return images[materialId] = builder.AddImage(ModelImageDecoder.Decode(data));

                    if (objects.GetValueOrDefault(materialId) is { } material
                        && Unity?.Image(NameOf(material)) is { Length: > 0 } packed)
                    {
                        return images[materialId] = builder.AddImage(ModelImageDecoder.Decode(packed));
                    }
                }
                catch (Exception)
                {
                }

                return -1;
            }

            private UnityPackage? Unity
            {
                get
                {
                    if (!searchedUnity)
                    {
                        searchedUnity = true;
                        unity = UnityPackage.Open(directory, MaterialNames());
                    }

                    return unity;
                }
            }

            private List<string> MaterialNames()
            {
                var names = new List<string>();

                foreach (var (_, node) in objects)
                {
                    if (node.Name == "Material" && NameOf(node) is { Length: > 0 } name && !names.Contains(name))
                        names.Add(name);
                }

                return names;
            }

            private int ImageOfSlot(long materialId, params string[] names)
            {
                var key = (materialId, names[0]);

                if (slotImages.TryGetValue(key, out var known))
                    return known;

                slotImages[key] = -1;

                try
                {
                    if (FindTexture(materialId, names) is { } texture
                        && ImageBytes(texture) is { Length: > 0 } data)
                    {
                        return slotImages[key] = builder.AddImage(ModelImageDecoder.Decode(data));
                    }
                }
                catch (Exception)
                {
                }

                return -1;
            }

            private FbxNode? FindTexture(long materialId, string[] names)
            {
                foreach (var (child, property) in propertiesOf.GetValueOrDefault(materialId) ?? [])
                {
                    if (objects.GetValueOrDefault(child) is not { Name: "Texture" } texture)
                        continue;

                    foreach (var name in names)
                    {
                        if (property.Contains(name, StringComparison.OrdinalIgnoreCase))
                            return texture;
                    }
                }

                return null;
            }

            private FbxNode? FindTexture(long materialId)
            {
                FbxNode? fallback = null;

                foreach (var (child, property) in propertiesOf.GetValueOrDefault(materialId) ?? [])
                {
                    if (objects.GetValueOrDefault(child) is not { Name: "Texture" } texture)
                        continue;

                    if (property.Contains("Diffuse", StringComparison.OrdinalIgnoreCase)
                        || property.Contains("BaseColor", StringComparison.OrdinalIgnoreCase))
                    {
                        return texture;
                    }

                    fallback ??= texture;
                }

                return fallback;
            }

            private byte[]? ImageBytes(FbxNode texture)
            {
                foreach (var child in childrenOf.GetValueOrDefault(texture.Number(0)) ?? [])
                {
                    if (objects.GetValueOrDefault(child) is { Name: "Video" } video
                        && video.Child("Content")?.Properties.FirstOrDefault() is byte[] { Length: > 0 } content)
                    {
                        return content;
                    }
                }

                foreach (var name in new[] { "RelativeFilename", "FileName" })
                {
                    var reference = texture.TextOf(name);

                    if (reference.Length == 0)
                        continue;

                    if (ObjModelLoader.FindFile(reference, [directory, Path.Combine(directory, "..")]) is { } found)
                        return File.ReadAllBytes(found);
                }

                return null;
            }
        }
    }
}
