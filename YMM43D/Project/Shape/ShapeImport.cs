using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YMM43D.Graphics.Meshes;
using YMM43D.Graphics.Models;

namespace YMM43D.Project.Shape
{
    internal static class ShapeImport
    {
        private const float Weld = 1e-5f;
        private const float SameNormal = 0.999f;
        private const float Shine = 1000f;

        private static readonly Dictionary<string, int> OptionArguments = new(StringComparer.OrdinalIgnoreCase)
        {
            ["-bm"] = 1, ["-blendu"] = 1, ["-blendv"] = 1, ["-boost"] = 1, ["-cc"] = 1, ["-clamp"] = 1,
            ["-imfchan"] = 1, ["-texres"] = 1, ["-type"] = 1, ["-mm"] = 2, ["-o"] = 3, ["-s"] = 3, ["-t"] = 3,
        };

        public sealed record Imported(MeshTopology Mesh, IReadOnlyList<ShapeMaterial> Materials);

        public static string Filter => "3D モデル (*.obj;*.gltf;*.glb;*.vrm;*.fbx;*.pmx)|*.obj;*.gltf;*.glb;*.vrm;*.fbx;*.pmx";

        public static Imported Load(string path)
            => string.Equals(Path.GetExtension(path), ".obj", StringComparison.OrdinalIgnoreCase) ? Obj(path) : Model(path);

        private static Imported Obj(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
            var positions = new List<Vector3>();
            var texCoords = new List<Vector2>();
            var faces = new List<int[]>();
            var faceUvs = new List<Vector2[]?>();
            var groups = new List<int>();
            var smooth = new List<bool>();
            var lines = new List<MeshEdge>();
            var library = new Dictionary<string, ShapeMaterial>(StringComparer.Ordinal);
            var used = new List<string>();
            var group = -1;
            var smoothing = false;

            foreach (var raw in ObjModelLoader.ReadLines(path))
            {
                var line = raw.Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                switch (parts[0])
                {
                    case "v" when parts.Length >= 4:
                        positions.Add(new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])));
                        break;

                    case "vt" when parts.Length >= 3:
                        texCoords.Add(new Vector2(Number(parts[1]), 1f - Number(parts[2])));
                        break;

                    case "mtllib":
                        foreach (var file in Rest(line, parts[0]).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            ReadMaterials(Path.Combine(directory, file.Trim('"')), library);

                        break;

                    case "usemtl":
                        var name = Rest(line, parts[0]);
                        group = used.IndexOf(name);

                        if (group < 0)
                        {
                            used.Add(name);
                            group = used.Count - 1;
                        }

                        break;

                    case "s":
                        smoothing = parts.Length > 1 && parts[1] is not ("off" or "0");
                        break;

                    case "l":
                        var chain = parts.Skip(1).Select(token => Index(token.Split('/')[0], positions.Count)).ToArray();

                        for (var i = 0; i + 1 < chain.Length; i++)
                            lines.Add(new MeshEdge(chain[i], chain[i + 1]));

                        break;

                    case "f" when parts.Length >= 4:
                        var corners = parts.Skip(1).Select(token => token.Split('/')).ToArray();
                        var points = corners.Select(corner => Index(corner[0], positions.Count)).ToArray();

                        if (points.Distinct().Count() < 3 || points.Any(point => point < 0 || point >= positions.Count))
                            break;

                        var hasUv = corners.All(corner => corner.Length > 1 && corner[1].Length > 0);
                        var uvs = hasUv ? corners.Select(corner => Index(corner[1], texCoords.Count)).Select(at => at >= 0 && at < texCoords.Count ? texCoords[at] : Vector2.Zero).ToArray() : null;

                        faces.Add(Reversed(points));
                        faceUvs.Add(uvs is null ? null : Reversed(uvs));
                        groups.Add(Math.Max(group, 0));
                        smooth.Add(smoothing);
                        break;
                }
            }

            var materials = used.Select(name => library.TryGetValue(name, out var found) ? found : new ShapeMaterial { Name = name }).ToList();
            Vector2[][]? allUvs = faceUvs.All(uvs => uvs is not null) ? [.. faceUvs.Select(uvs => uvs!)] : null;

            return new Imported(Compact(positions, faces, groups, smooth, lines, allUvs, null), materials);
        }

        private static Imported Model(string path)
        {
            var model = ModelLibrary.Load(path);
            var places = new Dictionary<(long, long, long), int>();
            var points = new List<Vector3>();
            var faces = new List<int[]>();
            var uvs = new List<Vector2[]>();
            var normals = new List<Vector3[]>();
            var groups = new List<int>();
            var slots = new Dictionary<int, int>();
            var materials = new List<ShapeMaterial>();
            var textures = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty, Path.GetFileNameWithoutExtension(path) + "_textures");

            int Point(Vector3 position)
            {
                var key = ((long)MathF.Round(position.X / Weld), (long)MathF.Round(position.Y / Weld), (long)MathF.Round(position.Z / Weld));

                if (!places.TryGetValue(key, out var found))
                {
                    found = places[key] = points.Count;
                    points.Add(position);
                }

                return found;
            }

            for (var p = 0; p < model.Parts.Length; p++)
            {
                var part = model.Parts[p];
                var key = part.Material >= 0 ? part.Material : -1 - p;

                if (!slots.TryGetValue(key, out var slot))
                {
                    slot = slots[key] = materials.Count;
                    materials.Add(MaterialOf(model, part, slot, textures));
                }

                for (var i = part.IndexStart; i + 2 < part.IndexStart + part.IndexCount && i + 2 < model.Indices.Length; i += 3)
                {
                    var corners = new[] { model.Indices[i], model.Indices[i + 2], model.Indices[i + 1] }.Select(index => model.Vertices[index]).ToArray();
                    var face = corners.Select(vertex => Point(vertex.Position)).ToArray();

                    if (face.Distinct().Count() < 3)
                        continue;

                    faces.Add(face);
                    uvs.Add([.. corners.Select(vertex => vertex.TexCoord)]);
                    normals.Add([.. corners.Select(vertex => vertex.Normal)]);
                    groups.Add(slot);
                }
            }

            var mesh = Compact(points, faces, groups, [.. faces.Select(_ => true)], [], [.. uvs], null);
            var marks = new Dictionary<MeshEdge, EdgeMark>();

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                if (mesh.EdgeFaces[e] is not [var first, var second])
                    continue;

                var edge = mesh.Edges[e];

                foreach (var vertex in new[] { edge.A, edge.B })
                {
                    var a = normals[first][Array.IndexOf(mesh.Faces[first], vertex)];
                    var b = normals[second][Array.IndexOf(mesh.Faces[second], vertex)];

                    if (a.LengthSquared() > 1e-12f && b.LengthSquared() > 1e-12f && Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)) < SameNormal)
                    {
                        marks[edge] = new EdgeMark(true, 0f);
                        break;
                    }
                }
            }

            return new Imported(mesh.WithMarks(marks), materials);
        }

        private static MeshTopology Compact(List<Vector3> positions, List<int[]> faces, List<int> groups, List<bool> smooth, List<MeshEdge> lines, Vector2[][]? uvs, Vector3[][]? keys)
        {
            var moved = new Dictionary<int, int>();
            var points = new List<Vector3>();

            int Move(int index)
            {
                if (!moved.TryGetValue(index, out var found))
                {
                    found = moved[index] = points.Count;
                    points.Add(positions[index]);
                }

                return found;
            }

            int[][] remapped = [.. faces.Select(face => face.Select(Move).ToArray())];
            MeshEdge[] loose = [.. lines.Where(line => line.A >= 0 && line.B >= 0 && line.A < positions.Count && line.B < positions.Count && line.A != line.B).Select(line => new MeshEdge(Move(line.A), Move(line.B)))];

            return new MeshTopology([.. points], remapped, [.. groups], [.. smooth], 1, loose, null, uvs, keys);
        }

        private static ShapeMaterial MaterialOf(ModelData model, ModelPart part, int slot, string textures)
        {
            var name = part.Material >= 0 && part.Material < model.Materials.Length ? model.Materials[part.Material] : $"材質 {slot + 1}";
            var color = part.Color;
            var surface = part.Surface;
            var material = new ShapeMaterial
            {
                Name = name,
                Color = Color.FromArgb(Byte(color.W), Byte(color.X), Byte(color.Y), Byte(color.Z)),
                Emission = Color.FromRgb(Byte(Math.Min(surface.Glow.X, 1f)), Byte(Math.Min(surface.Glow.Y, 1f)), Byte(Math.Min(surface.Glow.Z, 1f))),
                Image = part.Image >= 0 && part.Image < model.Images.Length ? Saved(model.Images[part.Image], textures, $"{slot + 1}_{name}") : string.Empty,
                NormalImage = surface.BumpImage >= 0 && surface.BumpImage < model.Images.Length ? Saved(model.Images[surface.BumpImage], textures, $"{slot + 1}_{name}_normal") : string.Empty,
            };

            material.Metallic.Values[0].Value = surface.Metallic * 100f;
            material.Roughness.Values[0].Value = surface.Roughness * 100f;
            material.NormalStrength.Values[0].Value = surface.BumpScale * 100f;
            material.ImageScaleX.Values[0].Value = surface.UvScale.X * 100f;
            material.ImageScaleY.Values[0].Value = surface.UvScale.Y * 100f;
            material.ImageOffsetX.Values[0].Value = surface.UvOffset.X * 100f;
            material.ImageOffsetY.Values[0].Value = surface.UvOffset.Y * 100f;

            return material;
        }

        private static string Saved(ModelImage image, string folder, string name)
        {
            var safe = string.Concat(name.Select(letter => Path.GetInvalidFileNameChars().Contains(letter) ? '_' : letter));
            var file = Path.Combine(folder, safe + ".png");

            Directory.CreateDirectory(folder);

            var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
            var encoder = new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var stream = File.Create(file))
                encoder.Save(stream);

            return file;
        }

        private static void ReadMaterials(string path, Dictionary<string, ShapeMaterial> library)
        {
            if (!File.Exists(path))
                return;

            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            ShapeMaterial? current = null;
            var roughnessGiven = false;

            foreach (var raw in ObjModelLoader.ReadLines(path))
            {
                var line = raw.Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                if (parts[0] == "newmtl")
                {
                    current = library[Rest(line, parts[0])] = new ShapeMaterial { Name = Rest(line, parts[0]) };
                    roughnessGiven = false;
                    continue;
                }

                if (current is null)
                    continue;

                switch (parts[0])
                {
                    case "Kd" when parts.Length >= 4:
                        current.Color = Color.FromArgb(current.Color.A, Byte(Number(parts[1])), Byte(Number(parts[2])), Byte(Number(parts[3])));
                        break;

                    case "d" when parts.Length >= 2:
                        current.Color = current.Color with { A = Byte(Number(parts[1])) };
                        break;

                    case "Tr" when parts.Length >= 2:
                        current.Color = current.Color with { A = Byte(1f - Number(parts[1])) };
                        break;

                    case "Ke" when parts.Length >= 4:
                        current.Emission = Color.FromRgb(Byte(Number(parts[1])), Byte(Number(parts[2])), Byte(Number(parts[3])));
                        break;

                    case "Pm" when parts.Length >= 2:
                        current.Metallic.Values[0].Value = Number(parts[1]) * 100f;
                        break;

                    case "Pr" when parts.Length >= 2:
                        current.Roughness.Values[0].Value = Number(parts[1]) * 100f;
                        roughnessGiven = true;
                        break;

                    case "Ns" when parts.Length >= 2 && !roughnessGiven:
                        current.Roughness.Values[0].Value = Math.Clamp(1f - Number(parts[1]) / Shine, 0f, 1f) * 100f;
                        break;

                    case "map_Kd":
                        current.Image = Texture(directory, parts);
                        Place(current, parts);
                        break;

                    case "norm":
                        current.NormalImage = Texture(directory, parts);
                        break;

                    case "map_Bump" or "map_bump" or "bump" when current.NormalImage.Length == 0:
                        current.NormalImage = Texture(directory, parts);
                        break;
                }
            }
        }

        private static string Texture(string directory, string[] parts)
        {
            var at = 1;

            while (at < parts.Length && parts[at].StartsWith('-') && OptionArguments.TryGetValue(parts[at], out var count))
                at += 1 + count;

            var file = string.Join(' ', parts.Skip(at)).Trim('"');

            if (file.Length == 0)
                return string.Empty;

            var full = Path.IsPathRooted(file) ? file : Path.Combine(directory, file);

            return File.Exists(full) ? full : string.Empty;
        }

        private static void Place(ShapeMaterial material, string[] parts)
        {
            var scale = Vector2.One;
            var offset = Vector2.Zero;

            for (var i = 1; i < parts.Length && parts[i].StartsWith('-') && OptionArguments.TryGetValue(parts[i], out var count); i += 1 + count)
            {
                if (parts[i] == "-s" && i + 2 < parts.Length)
                    scale = new Vector2(Number(parts[i + 1]), Number(parts[i + 2]));

                if (parts[i] == "-o" && i + 2 < parts.Length)
                    offset = new Vector2(Number(parts[i + 1]), Number(parts[i + 2]));
            }

            material.ImageScaleX.Values[0].Value = scale.X * 100f;
            material.ImageScaleY.Values[0].Value = scale.Y * 100f;
            material.ImageOffsetX.Values[0].Value = offset.X * 100f;
            material.ImageOffsetY.Values[0].Value = (offset == Vector2.Zero && scale.Y == 1f ? 0f : 1f - scale.Y - offset.Y) * 100f;
        }

        private static T[] Reversed<T>(T[] corners) => [corners[0], .. corners.Skip(1).Reverse()];

        private static string Rest(string line, string head) => line[head.Length..].Trim();

        private static int Index(string token, int count)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return -1;

            return value < 0 ? count + value : value - 1;
        }

        private static float Number(string token) => float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;

        private static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
    }
}
