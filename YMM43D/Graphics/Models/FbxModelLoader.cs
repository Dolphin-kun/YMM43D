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

            // id で引ける物の一覧と、つながり（子 → 親）。FBX は物とつながりが別々に書かれている。
            private readonly Dictionary<long, FbxNode> objects = [];
            private readonly Dictionary<long, List<long>> childrenOf = [];
            private readonly Dictionary<long, List<(long Child, string Property)>> propertiesOf = [];
            private readonly Dictionary<long, long> parentOf = [];
            private readonly Dictionary<long, int> images = [];
            private readonly Dictionary<long, Matrix4x4> worlds = [];

            public ModelData Read()
            {
                Collect();

                var axis = AxisMatrix();

                foreach (var (id, node) in objects)
                {
                    if (node.Name != "Model")
                        continue;

                    foreach (var child in childrenOf.GetValueOrDefault(id) ?? [])
                    {
                        if (objects.GetValueOrDefault(child) is { Name: "Geometry" } geometry)
                            AddGeometry(geometry, id, GeometricMatrix(node) * WorldOf(id) * axis);
                    }
                }

                return builder.Build();
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
                        parentOf.TryAdd(child, parent);
                    }
                    else if (kind == "OP")
                    {
                        (propertiesOf.TryGetValue(parent, out var list) ? list : propertiesOf[parent] = [])
                            .Add((child, link.Text(3)));
                    }
                }
            }

            // ファイルが決めている「上」と「前」を、このプラグインの向き（Y が上・Z が手前）に合わせる。
            private Matrix4x4 AxisMatrix()
            {
                if (root.Child("GlobalSettings")?.Child("Properties70") is not { } settings)
                    return Matrix4x4.Identity;

                var up = (int)Value(settings, "UpAxis", 1);
                var upSign = Value(settings, "UpAxisSign", 1) < 0 ? -1f : 1f;
                var front = (int)Value(settings, "FrontAxis", 2);
                var frontSign = Value(settings, "FrontAxisSign", 1) < 0 ? -1f : 1f;
                var right = (int)Value(settings, "CoordAxis", 0);
                var rightSign = Value(settings, "CoordAxisSign", 1) < 0 ? -1f : 1f;

                if (up == front || up == right || front == right)
                    return Matrix4x4.Identity;

                var matrix = Matrix4x4.Identity;

                SetRow(ref matrix, right, new Vector3(rightSign, 0f, 0f));
                SetRow(ref matrix, up, new Vector3(0f, upSign, 0f));
                SetRow(ref matrix, front, new Vector3(0f, 0f, frontSign));

                return matrix;
            }

            private static void SetRow(ref Matrix4x4 matrix, int index, Vector3 row)
            {
                switch (index)
                {
                    case 0: matrix.M11 = row.X; matrix.M12 = row.Y; matrix.M13 = row.Z; break;
                    case 1: matrix.M21 = row.X; matrix.M22 = row.Y; matrix.M23 = row.Z; break;
                    default: matrix.M31 = row.X; matrix.M32 = row.Y; matrix.M33 = row.Z; break;
                }
            }

            private Matrix4x4 WorldOf(long id)
            {
                if (worlds.TryGetValue(id, out var known))
                    return known;

                worlds[id] = Matrix4x4.Identity;

                var node = objects.GetValueOrDefault(id);
                var local = node is null ? Matrix4x4.Identity : LocalMatrix(node);

                var parent = parentOf.GetValueOrDefault(id);
                var world = parent != 0 && objects.GetValueOrDefault(parent) is { Name: "Model" }
                    ? local * WorldOf(parent)
                    : local;

                return worlds[id] = world;
            }

            // FBX の置き方は、回転や拡大の中心をずらす項目まで含めて決まっている。
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

            // メッシュだけに掛かり、子には伝わらない変換。
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

            private void AddGeometry(FbxNode geometry, long modelId, in Matrix4x4 world)
            {
                var positions = geometry.DoublesOf("Vertices");
                var polygons = geometry.IntegersOf("PolygonVertexIndex");

                if (positions.Length < 3 || polygons.Length < 3)
                    return;

                var pointCount = positions.Length / 3;

                Matrix4x4.Invert(world, out var inverse);
                var normalMatrix = Matrix4x4.Transpose(inverse);

                var normals = Layer(geometry, "LayerElementNormal", "Normals", "NormalsIndex");
                var texCoords = Layer(geometry, "LayerElementUV", "UV", "UVIndex");
                var materials = MaterialLayer(geometry);
                var slots = MaterialSlots(modelId);

                // 材質ごとに三角形をまとめてから積む。FBX は多角形ごとに材質が変わる。
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

                foreach (var (slot, triangles) in groups)
                {
                    var (color, image, key, name) = Material(slots, slot);

                    builder.BeginPart(color, image, key, name);

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
                                    world);

                                var normal = normals.Vector3At(normalAt);
                                normal = Vector3.TransformNormal(normal, normalMatrix);

                                var uv = texCoords.Vector2At(texCoordAt);

                                made[c] = known[vertexKey] = builder.AddVertex(
                                    position,
                                    normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.Zero,
                                    new Vector2(uv.X, 1f - uv.Y),
                                    Vector4.One);
                            }
                        }

                        builder.AddTriangle(made[0], made[1], made[2]);

                        if (!normals.HasValues)
                            FillFlatNormal(made[0], made[1], made[2]);
                    }
                }
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

            // 法線や UV は「角ごと」「頂点ごと」「多角形ごと」のどれかで並んでいる。
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

                // 画像があるときの色は、書き出し元によっては見た目用の暗い色が入っていて、
                // 掛け合わせると真っ黒になる。画像があるならそのまま使う。
                if (image < 0 && material.Child("Properties70") is { } properties)
                {
                    var diffuse = Vector(properties, "DiffuseColor", Vector(properties, "Diffuse", Vector3.One));
                    color = new Vector4(diffuse, 1f);
                }

                return (color, image, $"#{id}", NameOf(material));
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

                if (FindTexture(materialId) is not { } texture)
                    return -1;

                try
                {
                    if (ImageBytes(texture) is { Length: > 0 } data)
                        return images[materialId] = builder.AddImage(ModelImageDecoder.Decode(data));
                }
                catch (Exception)
                {
                }

                return -1;
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
                // 画像がファイルに埋め込まれていることがある。あればそれを使う。
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
