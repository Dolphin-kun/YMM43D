using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace YMM43D.Graphics.Models
{
    internal sealed partial class UnityPackage
    {
        private const long SmallAsset = 1 << 20;

        private const long KeepAtMost = 32L << 20;

        private static readonly string[] MainTextureNames =
            ["_MainTex", "_BaseMap", "_BaseColorMap", "_MainTexture"];

        private static readonly string[] ImageKinds = [".png", ".tga", ".jpg", ".jpeg", ".bmp", ".psd"];

        private readonly Dictionary<string, byte[]> images = [];

        public byte[]? Image(string material) => images.GetValueOrDefault(material);

        public bool IsEmpty => images.Count == 0;

        public static UnityPackage? Open(string directory, IReadOnlyList<string> materials)
        {
            if (materials.Count == 0)
                return null;

            foreach (var package in Packages(directory))
            {
                try
                {
                    var read = new UnityPackage();

                    read.Read(package, materials);

                    if (!read.IsEmpty)
                        return read;
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        private static IEnumerable<string> Packages(string directory)
        {
            var at = directory;

            for (var depth = 0; depth < 3 && at.Length > 0 && Directory.Exists(at); depth++)
            {
                foreach (var package in Directory.EnumerateFiles(at, "*.unitypackage"))
                    yield return package;

                at = Path.GetDirectoryName(at) ?? string.Empty;
            }
        }

        private void Read(string package, IReadOnlyList<string> materials)
        {
            var paths = new Dictionary<string, string>();
            var kept = new Dictionary<string, byte[]>();

            Scan(package, (guid, kind, read) =>
            {
                if (kind == "pathname")
                    paths[guid] = Encoding.UTF8.GetString(read()).Split('\n')[0].Trim();
                else if (kind == "asset")
                    kept[guid] = read();
            },
            (guid, kind, length) => kind switch
            {
                "pathname" => true,
                "asset" => length <= SmallAsset && kept.Values.Sum(one => (long)one.Length) < KeepAtMost,
                _ => false,
            });

            var wanted = new Dictionary<string, string>();

            foreach (var material in materials)
            {
                if (images.ContainsKey(material) || TextureOf(paths, kept, material) is not { } guid)
                    continue;

                if (kept.TryGetValue(guid, out var already))
                    images[material] = already;
                else
                    wanted[guid] = material;
            }

            if (wanted.Count == 0)
                return;

            Scan(package, (guid, _, read) => images[wanted[guid]] = read(),
                (guid, kind, _) => kind == "asset" && wanted.ContainsKey(guid));
        }

        private static void Scan(
            string package,
            Action<string, string, Func<byte[]>> onEntry,
            Func<string, string, long, bool> wants)
        {
            using var file = File.OpenRead(package);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);

            while (tar.GetNextEntry(copyData: false) is { } entry)
            {
                var name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
                var slash = name.IndexOf('/');

                if (slash <= 0 || entry.DataStream is not { } stream)
                    continue;

                var guid = name[..slash];
                var kind = name[(slash + 1)..];

                if (!wants(guid, kind, entry.Length))
                    continue;

                onEntry(guid, kind, () =>
                {
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);

                    return memory.ToArray();
                });
            }
        }

        private static string? TextureOf(
            Dictionary<string, string> paths, Dictionary<string, byte[]> kept, string material)
        {
            foreach (var (guid, path) in paths)
            {
                if (!Is(path, material + ".mat") || !kept.TryGetValue(guid, out var content))
                    continue;

                if (MainTexture(Encoding.UTF8.GetString(content)) is { } found && paths.ContainsKey(found))
                    return found;
            }

            var best = (Guid: (string?)null, Score: 0);

            foreach (var (guid, path) in paths)
            {
                var name = Path.GetFileNameWithoutExtension(path);

                if (!ImageKinds.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    || !name.StartsWith(material, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = name.Contains("base", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("albedo", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("main", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

                if (score > best.Score)
                    best = (guid, score);
            }

            return best.Guid;
        }

        private static bool Is(string path, string name)
            => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase);

        private static string? MainTexture(string material)
        {
            foreach (var name in MainTextureNames)
            {
                var match = TextureLine().Match(material, 0);

                while (match.Success)
                {
                    if (match.Groups[1].Value == name)
                        return match.Groups[2].Value;

                    match = match.NextMatch();
                }
            }

            return null;
        }

        [GeneratedRegex(@"-\s*(_\w+):\s*\r?\n\s*m_Texture:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})")]
        private static partial Regex TextureLine();
    }
}
