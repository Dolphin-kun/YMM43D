using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Unicode;

namespace YMM43D.Graphics.Models
{
    public static class ObjModelLoader
    {
        private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif"];

        private static readonly Lazy<Encoding> ShiftJis = new(() =>
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        });

        private readonly record struct Material(Vector4 Color, string? Texture, string Directory);

        private readonly record struct Corner(int Position, int TexCoord, int Normal);

        public static ModelData Load(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;

            var positions = new List<Vector3>();
            var colors = new List<Vector4>();
            var texCoords = new List<Vector2>();
            var normals = new List<Vector3>();
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            var images = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var hasLibrary = false;
            var triedSibling = false;

            var builder = new ModelBuilder();
            var shared = new Dictionary<Corner, uint>();
            var corners = new List<Corner>();

            builder.BeginPart(Vector4.One, -1, ModelData.NoMaterial, ModelData.NoMaterial);

            foreach (var raw in ReadLines(fullPath))
            {
                var line = raw.Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                switch (parts[0])
                {
                    case "v":
                        positions.Add(new Vector3(Number(parts, 1), Number(parts, 2), Number(parts, 3)));
                        colors.Add(parts.Length >= 7
                            ? new Vector4(Number(parts, 4), Number(parts, 5), Number(parts, 6), 1f)
                            : Vector4.One);
                        break;

                    case "vt":
                        texCoords.Add(new Vector2(Number(parts, 1), 1f - Number(parts, 2)));
                        break;

                    case "vn":
                        normals.Add(new Vector3(Number(parts, 1), Number(parts, 2), Number(parts, 3)));
                        break;

                    case "mtllib":
                        foreach (var library in Libraries(line, parts, directory))
                        {
                            ReadMaterials(library, materials);
                            hasLibrary = true;
                        }
                        break;

                    case "usemtl":
                        if (!hasLibrary && !triedSibling)
                        {
                            triedSibling = true;

                            var sibling = Path.ChangeExtension(fullPath, ".mtl");

                            if (File.Exists(sibling))
                                ReadMaterials(sibling, materials);
                        }

                        var name = RestOf(line);
                        var material = materials.TryGetValue(name, out var found)
                            ? found
                            : new Material(Vector4.One, null, directory);

                        builder.BeginPart(
                            material.Color, ImageOf(material, directory, images, builder), name, name);
                        shared.Clear();
                        break;

                    case "f":
                        corners.Clear();

                        for (var i = 1; i < parts.Length; i++)
                            corners.Add(ParseCorner(parts[i], positions.Count, texCoords.Count, normals.Count));

                        AddFace(builder, corners, shared, positions, colors, texCoords, normals);
                        break;
                }
            }

            return builder.Build();
        }

        internal static IEnumerable<string> ReadLines(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var reader = new StreamReader(new MemoryStream(bytes, writable: false), DetectEncoding(bytes), true);

            while (reader.ReadLine() is { } line)
                yield return line;
        }

        internal static Encoding DetectEncoding(ReadOnlySpan<byte> bytes)
        {
            if (bytes is [0xEF, 0xBB, 0xBF, ..])
                return Encoding.UTF8;

            if (bytes is [0xFF, 0xFE, ..])
                return Encoding.Unicode;

            if (bytes is [0xFE, 0xFF, ..])
                return Encoding.BigEndianUnicode;

            return Utf8.IsValid(bytes) ? Encoding.UTF8 : ShiftJis.Value;
        }

        private static void AddFace(
            ModelBuilder builder,
            List<Corner> corners,
            Dictionary<Corner, uint> shared,
            List<Vector3> positions,
            List<Vector4> colors,
            List<Vector2> texCoords,
            List<Vector3> normals)
        {
            if (corners.Count < 3)
                return;

            var faceNormal = FaceNormal(corners, positions);
            var indices = new uint[corners.Count];

            for (var i = 0; i < corners.Count; i++)
            {
                var corner = corners[i];

                if (corner.Normal >= 0 && shared.TryGetValue(corner, out var existing))
                {
                    indices[i] = existing;
                    continue;
                }

                var index = builder.AddVertex(
                    positions[corner.Position],
                    corner.Normal >= 0 ? normals[corner.Normal] : faceNormal,
                    corner.TexCoord >= 0 ? texCoords[corner.TexCoord] : Vector2.Zero,
                    colors[corner.Position]);

                if (corner.Normal >= 0)
                    shared[corner] = index;

                indices[i] = index;
            }

            for (var i = 1; i + 1 < indices.Length; i++)
                builder.AddTriangle(indices[0], indices[i], indices[i + 1]);
        }

        private static Vector3 FaceNormal(List<Corner> corners, List<Vector3> positions)
        {
            var sum = Vector3.Zero;
            var origin = positions[corners[0].Position];

            for (var i = 1; i + 1 < corners.Count; i++)
            {
                sum += Vector3.Cross(
                    positions[corners[i].Position] - origin,
                    positions[corners[i + 1].Position] - origin);
            }

            return sum.LengthSquared() > 1e-20f ? Vector3.Normalize(sum) : Vector3.UnitZ;
        }

        private static Corner ParseCorner(string token, int positionCount, int texCoordCount, int normalCount)
        {
            var pieces = token.Split('/');

            return new Corner(
                Resolve(pieces[0], positionCount) ?? throw new InvalidDataException($"面の頂点番号 {token} が読めません。"),
                pieces.Length > 1 ? Resolve(pieces[1], texCoordCount) ?? -1 : -1,
                pieces.Length > 2 ? Resolve(pieces[2], normalCount) ?? -1 : -1);
        }

        private static int? Resolve(string text, int count)
        {
            if (text.Length == 0 || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                return null;

            var index = number < 0 ? count + number : number - 1;

            if (index < 0 || index >= count)
                throw new InvalidDataException($"番号 {number} は範囲の外です（{count} 個）。");

            return index;
        }

        private static IEnumerable<string> Libraries(string line, string[] parts, string directory)
        {
            if (FindFile(RestOf(line), [directory]) is { } whole)
            {
                yield return whole;
                yield break;
            }

            for (var i = 1; i < parts.Length; i++)
            {
                if (FindFile(parts[i], [directory]) is { } library)
                    yield return library;
            }
        }

        private static void ReadMaterials(string path, Dictionary<string, Material> materials)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;

            string? name = null;
            var color = Vector4.One;
            string? texture = null;

            void Flush()
            {
                if (name is not null)
                    materials[name] = new Material(color, texture, directory);
            }

            foreach (var raw in ReadLines(path))
            {
                var line = raw.Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                switch (parts[0].ToLowerInvariant())
                {
                    case "newmtl":
                        Flush();
                        name = RestOf(line);
                        color = Vector4.One;
                        texture = null;
                        break;

                    case "kd":
                        color = new Vector4(Number(parts, 1), Number(parts, 2), Number(parts, 3), color.W);
                        break;

                    case "d":
                        color.W = Number(parts, 1);
                        break;

                    case "tr":
                        color.W = 1f - Number(parts, 1);
                        break;

                    case "map_kd":
                        texture = TextureReference(line) ?? texture;
                        break;
                }
            }

            Flush();
        }

        internal static string? TextureReference(string line)
        {
            var tokens = new List<(int Start, string Text)>();

            for (var i = 0; i < line.Length;)
            {
                if (char.IsWhiteSpace(line[i]))
                {
                    i++;
                    continue;
                }

                var start = i;

                while (i < line.Length && !char.IsWhiteSpace(line[i]))
                    i++;

                tokens.Add((start, line[start..i]));
            }

            var index = 1;

            while (index < tokens.Count && tokens[index].Text is ['-', _, ..] option && !IsNumber(option))
            {
                index++;

                while (index < tokens.Count && (IsNumber(tokens[index].Text) || tokens[index].Text is "on" or "off"))
                    index++;
            }

            if (index >= tokens.Count)
                return null;

            var reference = line[tokens[index].Start..].Trim().Trim('"');

            return reference.Length > 0 ? reference : null;
        }

        private static int ImageOf(
            Material material, string modelDirectory, Dictionary<string, int> images, ModelBuilder builder)
        {
            if (material.Texture is not { } texture)
                return -1;

            var key = $"{material.Directory}|{texture}";

            if (images.TryGetValue(key, out var index))
                return index;

            index = -1;

            foreach (var candidate in Alternatives(texture))
            {
                if (FindFile(candidate, [material.Directory, modelDirectory]) is not { } path)
                    continue;

                try
                {
                    index = builder.AddImage(ModelImageDecoder.DecodeFile(path));
                    break;
                }
                catch (Exception error)
                {
                    Trace.TraceWarning($"[YMM43D] 3Dモデルの画像 {path} を読み込めませんでした。{error.Message}");
                }
            }

            if (index < 0)
                Trace.TraceWarning($"[YMM43D] 3Dモデルの画像 {texture} が見つかりません。");

            return images[key] = index;
        }

        private static IEnumerable<string> Alternatives(string texture)
        {
            yield return texture;

            var extension = Path.GetExtension(texture);

            foreach (var other in ImageExtensions)
            {
                if (!other.Equals(extension, StringComparison.OrdinalIgnoreCase))
                    yield return texture[..^extension.Length] + other;
            }
        }

        internal static string? FindFile(string reference, string[] directories)
        {
            var cleaned = reference.Trim().Trim('"')
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            if (cleaned.Length == 0)
                return null;

            try
            {
                foreach (var directory in directories)
                {
                    var direct = Path.Combine(directory, cleaned);

                    if (File.Exists(direct))
                        return Path.GetFullPath(direct);
                }

                var name = Path.GetFileName(cleaned);

                if (name.Length == 0 || name.IndexOfAny(['*', '?']) >= 0)
                    return null;

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 2,
                    IgnoreInaccessible = true,
                    MatchCasing = MatchCasing.CaseInsensitive,
                };

                foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(directory)
                        && Directory.EnumerateFiles(directory, name, options).FirstOrDefault() is { } nearby)
                    {
                        return nearby;
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static string RestOf(string line)
        {
            var space = line.IndexOfAny([' ', '\t']);

            return space < 0 ? string.Empty : line[space..].Trim();
        }

        private static bool IsNumber(string text)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        private static float Number(string[] parts, int index)
            => index < parts.Length && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f;
    }
}
