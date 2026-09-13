using System.Diagnostics;
using System.IO;

namespace YMM43D.Graphics.Models
{
    public static class ModelLibrary
    {
        private const int MaxCached = 8;

        private static readonly Lock gate = new();

        private static readonly List<(string Key, ModelData? Model)> cache = [];

        private static readonly List<(string Key, ModelImage? Image)> imageCache = [];

        public static bool IsSupported(string? path)
            => Path.GetExtension(path ?? string.Empty).ToLowerInvariant() is ".obj" or ".gltf" or ".glb";

        public static ModelData? Find(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsSupported(path))
                return null;

            return Cached(cache, path, MaxCached, TryLoad);
        }

        public static ModelImage? FindImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return Cached(imageCache, path, MaxCached * 4, TryLoadImage);
        }

        private static T? Cached<T>(List<(string Key, T? Value)> entries, string path, int limit, Func<string, T?> load)
            where T : class
        {
            string key;

            try
            {
                var info = new FileInfo(path);

                if (!info.Exists)
                    return null;

                key = $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            }
            catch (Exception)
            {
                return null;
            }

            lock (gate)
            {
                var index = entries.FindIndex(entry => entry.Key == key);

                if (index >= 0)
                {
                    var hit = entries[index];
                    entries.RemoveAt(index);
                    entries.Add(hit);
                    return hit.Value;
                }
            }

            var value = load(path);

            lock (gate)
            {
                entries.RemoveAll(entry => entry.Key == key);
                entries.Add((key, value));

                while (entries.Count > limit)
                    entries.RemoveAt(0);
            }

            return value;
        }

        private static ModelImage? TryLoadImage(string path)
        {
            try
            {
                return ModelImageDecoder.DecodeFile(path);
            }
            catch (Exception error)
            {
                Trace.TraceError($"[YMM43D] 画像 {path} を読み込めませんでした。{error.Message}");
                return null;
            }
        }

        public static ModelData Load(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => ObjModelLoader.Load(path),
            ".gltf" or ".glb" => GltfModelLoader.Load(path),
            var other => throw new NotSupportedException($"{other} は読み込めません。obj / gltf / glb を選んでください。"),
        };

        private static ModelData? TryLoad(string path)
        {
            try
            {
                return Load(path);
            }
            catch (Exception error)
            {
                Trace.TraceError($"[YMM43D] 3Dモデル {path} を読み込めませんでした。{error.Message}");
                return null;
            }
        }
    }
}
