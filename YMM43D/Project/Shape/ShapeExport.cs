using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics.Models;

namespace YMM43D.Project.Shape
{
    internal static class ShapeExport
    {
        private const int ArrayBuffer = 34962;
        private const int ElementBuffer = 34963;
        private const int FloatType = 5126;
        private const int UintType = 5125;
        private const int Repeat = 10497;
        private const int Linear = 9729;
        private const int LinearMipmap = 9987;
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinChunk = 0x004E4942;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static void WriteObj(string path, SurfaceGeometry geometry, IReadOnlyList<ShapeSlot> slots)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var library = name + ".mtl";
            var corners = ShapeCorners.Of(geometry);
            var text = new StringBuilder();
            var uvs = new Dictionary<Vector2, int>();
            var normals = new Dictionary<Vector3, int>();
            var uvLines = new StringBuilder();
            var normalLines = new StringBuilder();

            text.AppendLine("# YMM43D");
            text.AppendLine($"mtllib {library}");
            text.AppendLine($"o {name}");

            foreach (var point in geometry.Vertices)
                text.AppendLine(Invariant, $"v {point.X:R} {point.Y:R} {point.Z:R}");

            int Number<T>(Dictionary<T, int> known, T value, StringBuilder lines, Func<T, string> format) where T : notnull
            {
                if (known.TryGetValue(value, out var found))
                    return found;

                lines.AppendLine(format(value));

                return known[value] = known.Count + 1;
            }

            var faceLines = new StringBuilder();
            var order = Enumerable.Range(0, geometry.Faces.Length).OrderBy(face => SlotOf(geometry.Faces[face].Group, slots.Count)).ToArray();
            var slot = -1;
            var names = Names(slots);
            bool? smooth = null;

            foreach (var f in order)
            {
                var face = geometry.Faces[f];
                var here = SlotOf(face.Group, slots.Count);

                if (here != slot)
                {
                    slot = here;
                    faceLines.AppendLine($"usemtl {names[slot]}");
                }

                if (smooth != face.IsSmooth)
                {
                    smooth = face.IsSmooth;
                    faceLines.AppendLine(face.IsSmooth ? "s 1" : "s off");
                }

                faceLines.Append('f');

                foreach (var corner in ShapeCorners.Reversed(corners[f]))
                {
                    var uv = Number(uvs, corner.Uv, uvLines, value => string.Create(Invariant, $"vt {value.X:R} {1f - value.Y:R}"));
                    var normal = Number(normals, corner.Normal, normalLines, value => string.Create(Invariant, $"vn {value.X:R} {value.Y:R} {value.Z:R}"));

                    faceLines.Append($" {corner.Point + 1}/{uv}/{normal}");
                }

                faceLines.AppendLine();
            }

            text.Append(uvLines).Append(normalLines).Append(faceLines);
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, library), Materials(slots, directory), new UTF8Encoding(false));
        }

        public static void WriteGlb(string path, SurfaceGeometry geometry, IReadOnlyList<ShapeSlot> slots)
        {
            var corners = ShapeCorners.Of(geometry);
            var binary = new MemoryStream();
            var bufferViews = new JsonArray();
            var accessors = new JsonArray();
            var primitives = new JsonArray();
            var images = new JsonArray();
            var textures = new JsonArray();
            var imageNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var transforms = false;

            int View(ReadOnlySpan<byte> bytes, int? target)
            {
                while (binary.Length % 4 != 0)
                    binary.WriteByte(0);

                var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = binary.Length, ["byteLength"] = bytes.Length };

                if (target is { } kind)
                    view["target"] = kind;

                binary.Write(bytes);
                bufferViews.Add(view);

                return bufferViews.Count - 1;
            }

            int Accessor(int view, int componentType, int count, string type, JsonArray? min = null, JsonArray? max = null)
            {
                var accessor = new JsonObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };

                if (min is not null && max is not null)
                {
                    accessor["min"] = min;
                    accessor["max"] = max;
                }

                accessors.Add(accessor);

                return accessors.Count - 1;
            }

            int? Texture(string file)
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                    return null;

                if (!imageNumbers.TryGetValue(file, out var number))
                {
                    if (ImageBytes(file) is not { } encoded)
                        return null;

                    images.Add(new JsonObject { ["bufferView"] = View(encoded.Bytes, null), ["mimeType"] = encoded.Mime });
                    textures.Add(new JsonObject { ["sampler"] = 0, ["source"] = images.Count - 1 });
                    number = imageNumbers[file] = textures.Count - 1;
                }

                return number;
            }

            JsonObject Reference(int texture, ShapeSlot slot)
            {
                var reference = new JsonObject { ["index"] = texture };

                if (slot.UvScale != Vector2.One || slot.UvOffset != Vector2.Zero || slot.UvRotation != 0f)
                {
                    transforms = true;
                    reference["extensions"] = new JsonObject
                    {
                        ["KHR_texture_transform"] = new JsonObject
                        {
                            ["offset"] = new JsonArray(slot.UvOffset.X, slot.UvOffset.Y),
                            ["rotation"] = -slot.UvRotation,
                            ["scale"] = new JsonArray(slot.UvScale.X, slot.UvScale.Y),
                        },
                    };
                }

                return reference;
            }

            var materials = new JsonArray();

            foreach (var slot in slots)
            {
                var pbr = new JsonObject
                {
                    ["baseColorFactor"] = new JsonArray(slot.Color.X, slot.Color.Y, slot.Color.Z, slot.Color.W),
                    ["metallicFactor"] = slot.Metallic,
                    ["roughnessFactor"] = slot.Roughness,
                };

                if (Texture(slot.Image) is { } color)
                    pbr["baseColorTexture"] = Reference(color, slot);

                var material = new JsonObject
                {
                    ["name"] = slot.Name,
                    ["pbrMetallicRoughness"] = pbr,
                    ["doubleSided"] = true,
                };

                if (slot.Glow != Vector3.Zero)
                    material["emissiveFactor"] = new JsonArray(Math.Min(slot.Glow.X, 1f), Math.Min(slot.Glow.Y, 1f), Math.Min(slot.Glow.Z, 1f));

                if (Texture(slot.NormalImage) is { } normal)
                {
                    var reference = Reference(normal, slot);

                    reference["scale"] = slot.NormalStrength;
                    material["normalTexture"] = reference;
                }

                materials.Add(material);
            }

            for (var s = 0; s < slots.Count; s++)
            {
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var indices = new List<uint>();
                var numbers = new Dictionary<ShapeCorners.Corner, uint>();

                for (var f = 0; f < geometry.Faces.Length; f++)
                {
                    if (SlotOf(geometry.Faces[f].Group, slots.Count) != s)
                        continue;

                    var face = ShapeCorners.Reversed(corners[f]);
                    var ids = new uint[face.Length];

                    for (var i = 0; i < face.Length; i++)
                    {
                        if (!numbers.TryGetValue(face[i], out var id))
                        {
                            id = numbers[face[i]] = (uint)positions.Count;
                            positions.Add(geometry.Vertices[face[i].Point]);
                            normals.Add(face[i].Normal.LengthSquared() > 1e-12f ? Vector3.Normalize(face[i].Normal) : Vector3.UnitY);
                            uvs.Add(face[i].Uv);
                        }

                        ids[i] = id;
                    }

                    for (var i = 1; i + 1 < ids.Length; i++)
                        indices.AddRange([ids[0], ids[i], ids[i + 1]]);
                }

                if (indices.Count == 0)
                    continue;

                var min = positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
                var max = positions.Aggregate(new Vector3(float.MinValue), Vector3.Max);
                var position = Accessor(View(Bytes(positions), ArrayBuffer), FloatType, positions.Count, "VEC3", new JsonArray(min.X, min.Y, min.Z), new JsonArray(max.X, max.Y, max.Z));
                var normal = Accessor(View(Bytes(normals), ArrayBuffer), FloatType, normals.Count, "VEC3");
                var uv = Accessor(View(Bytes(uvs), ArrayBuffer), FloatType, uvs.Count, "VEC2");
                var index = Accessor(View(Bytes(indices), ElementBuffer), UintType, indices.Count, "SCALAR");

                primitives.Add(new JsonObject
                {
                    ["attributes"] = new JsonObject { ["POSITION"] = position, ["NORMAL"] = normal, ["TEXCOORD_0"] = uv },
                    ["indices"] = index,
                    ["material"] = s,
                });
            }

            while (binary.Length % 4 != 0)
                binary.WriteByte(0);

            var root = new JsonObject
            {
                ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "YMM43D" },
                ["scene"] = 0,
                ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }),
                ["nodes"] = new JsonArray(new JsonObject { ["name"] = Path.GetFileNameWithoutExtension(path), ["mesh"] = 0 }),
                ["meshes"] = new JsonArray(new JsonObject { ["name"] = Path.GetFileNameWithoutExtension(path), ["primitives"] = primitives }),
                ["materials"] = materials,
                ["accessors"] = accessors,
                ["bufferViews"] = bufferViews,
                ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = binary.Length }),
            };

            if (images.Count > 0)
            {
                root["images"] = images;
                root["textures"] = textures;
                root["samplers"] = new JsonArray(new JsonObject { ["magFilter"] = Linear, ["minFilter"] = LinearMipmap, ["wrapS"] = Repeat, ["wrapT"] = Repeat });
            }

            if (transforms)
                root["extensionsUsed"] = new JsonArray("KHR_texture_transform");

            var json = Encoding.UTF8.GetBytes(root.ToJsonString());
            var padded = (json.Length + 3) / 4 * 4;

            using var file = File.Create(path);
            using var writer = new BinaryWriter(file);

            writer.Write(GlbMagic);
            writer.Write(2u);
            writer.Write((uint)(12 + 8 + padded + 8 + binary.Length));
            writer.Write((uint)padded);
            writer.Write(JsonChunk);
            writer.Write(json);

            for (var i = json.Length; i < padded; i++)
                writer.Write((byte)' ');

            writer.Write((uint)binary.Length);
            writer.Write(BinChunk);
            writer.Write(binary.ToArray());
        }

        private static int SlotOf(int group, int count) => group >= 0 && group < count ? group : 0;

        private static string[] Names(IReadOnlyList<ShapeSlot> slots)
        {
            var names = new string[slots.Count];
            var taken = new HashSet<string>(StringComparer.Ordinal);

            for (var s = 0; s < slots.Count; s++)
            {
                var cleaned = new string([.. slots[s].Name.Select(letter => char.IsWhiteSpace(letter) ? '_' : letter)]);
                var name = cleaned.Length > 0 ? cleaned : $"material_{s + 1}";

                for (var n = 2; !taken.Add(name); n++)
                    name = $"{cleaned}_{n}";

                names[s] = name;
            }

            return names;
        }

        private static string Materials(IReadOnlyList<ShapeSlot> slots, string directory)
        {
            var text = new StringBuilder();

            text.AppendLine("# YMM43D");

            var names = Names(slots);

            for (var s = 0; s < slots.Count; s++)
            {
                var slot = slots[s];

                text.AppendLine();
                text.AppendLine($"newmtl {names[s]}");
                text.AppendLine(Invariant, $"Kd {slot.Color.X:R} {slot.Color.Y:R} {slot.Color.Z:R}");
                text.AppendLine(Invariant, $"Ks {slot.Gloss:R} {slot.Gloss:R} {slot.Gloss:R}");
                text.AppendLine(Invariant, $"Ns {(1f - slot.Roughness) * 1000f:R}");
                text.AppendLine(Invariant, $"Ke {slot.Glow.X:R} {slot.Glow.Y:R} {slot.Glow.Z:R}");
                text.AppendLine(Invariant, $"Pm {slot.Metallic:R}");
                text.AppendLine(Invariant, $"Pr {slot.Roughness:R}");

                if (slot.Color.W < 1f)
                    text.AppendLine(Invariant, $"d {slot.Color.W:R}");

                if (Copied(slot.Image, directory) is { } image)
                    text.AppendLine($"map_Kd {Placement(slot)}{image}");

                if (Copied(slot.NormalImage, directory) is { } normal)
                {
                    text.AppendLine(Invariant, $"map_Bump -bm {slot.NormalStrength:R} {normal}");
                    text.AppendLine($"norm {normal}");
                }
            }

            return text.ToString();
        }

        private static string Placement(ShapeSlot slot)
        {
            var text = new StringBuilder();

            if (slot.UvScale != Vector2.One)
                text.Append(Invariant, $"-s {slot.UvScale.X:R} {slot.UvScale.Y:R} 1 ");

            if (slot.UvOffset != Vector2.Zero || slot.UvScale.Y != 1f)
                text.Append(Invariant, $"-o {slot.UvOffset.X:R} {1f - slot.UvScale.Y - slot.UvOffset.Y:R} 0 ");

            return text.ToString();
        }

        private static string? Copied(string file, string directory)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                return null;

            var name = Path.GetFileName(file);
            var target = Path.Combine(directory, name);

            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(file, target, true);

            return name.Contains(' ') ? $"\"{name}\"" : name;
        }

        private static (byte[] Bytes, string Mime)? ImageBytes(string file)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();

            if (extension is ".png")
                return (File.ReadAllBytes(file), "image/png");

            if (extension is ".jpg" or ".jpeg")
                return (File.ReadAllBytes(file), "image/jpeg");

            if (ModelLibrary.FindImage(file) is not { } image)
                return null;

            var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
            var encoder = new PngBitmapEncoder();
            using var stream = new MemoryStream();

            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);

            return (stream.ToArray(), "image/png");
        }

        private static byte[] Bytes<T>(List<T> values) where T : unmanaged
            => System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values)).ToArray();
    }
}
