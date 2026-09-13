using System.Globalization;
using System.IO;
using System.Numerics;

namespace YMM43D.Graphics.Models
{
    public static class ObjModelLoader
    {
        private readonly record struct Material(Vector4 Color, string? Texture);

        private readonly record struct Corner(int Position, int TexCoord, int Normal);

        public static ModelData Load(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;

            var positions = new List<Vector3>();
            var colors = new List<Vector4>();
            var texCoords = new List<Vector2>();
            var normals = new List<Vector3>();
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            var images = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var builder = new ModelBuilder();
            var shared = new Dictionary<Corner, uint>();
            var corners = new List<Corner>();

            builder.BeginPart(Vector4.One, -1);

            foreach (var raw in File.ReadLines(path))
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
                        foreach (var library in Rest(line))
                            ReadMaterials(Path.Combine(directory, library), materials);
                        break;

                    case "usemtl":
                        var name = line[parts[0].Length..].Trim();
                        var material = materials.TryGetValue(name, out var found) ? found : new Material(Vector4.One, null);

                        builder.BeginPart(material.Color, ImageOf(material.Texture, directory, images, builder));
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

        private static void ReadMaterials(string path, Dictionary<string, Material> materials)
        {
            if (!File.Exists(path))
                return;

            string? name = null;
            var color = Vector4.One;
            string? texture = null;

            void Flush()
            {
                if (name is not null)
                    materials[name] = new Material(color, texture);
            }

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                switch (parts[0])
                {
                    case "newmtl":
                        Flush();
                        name = line[parts[0].Length..].Trim();
                        color = Vector4.One;
                        texture = null;
                        break;

                    case "Kd":
                        color = new Vector4(Number(parts, 1), Number(parts, 2), Number(parts, 3), color.W);
                        break;

                    case "d":
                        color.W = Number(parts, 1);
                        break;

                    case "Tr":
                        color.W = 1f - Number(parts, 1);
                        break;

                    case "map_Kd":
                        texture = parts.Length > 1 ? parts[^1] : null;
                        break;
                }
            }

            Flush();
        }

        private static int ImageOf(
            string? texture, string directory, Dictionary<string, int> images, ModelBuilder builder)
        {
            if (texture is null)
                return -1;

            var path = Path.Combine(directory, texture.Replace('\\', Path.DirectorySeparatorChar));

            if (images.TryGetValue(path, out var index))
                return index;

            try
            {
                index = File.Exists(path) ? builder.AddImage(ModelImageDecoder.DecodeFile(path)) : -1;
            }
            catch (Exception)
            {
                index = -1;
            }

            return images[path] = index;
        }

        private static IEnumerable<string> Rest(string line)
        {
            var space = line.IndexOfAny([' ', '\t']);

            return space < 0 ? [] : [line[space..].Trim()];
        }

        private static float Number(string[] parts, int index)
            => index < parts.Length && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f;
    }
}
