using System.Diagnostics;
using System.IO;

namespace YMM43D.Graphics.Models
{
    public static class ModelLibrary
    {
        private const int MaxCached = 8;

        private static readonly Lock gate = new();

        private static readonly List<(string Key, ModelData? Model)> cache = [];

        public static bool IsSupported(string? path)
            => Path.GetExtension(path ?? string.Empty).ToLowerInvariant() is ".obj" or ".gltf" or ".glb";

        public static ModelData? Find(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsSupported(path))
                return null;

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
                var index = cache.FindIndex(entry => entry.Key == key);

                if (index >= 0)
                {
                    var hit = cache[index];
                    cache.RemoveAt(index);
                    cache.Add(hit);
                    return hit.Model;
                }
            }

            var model = TryLoad(path);

            lock (gate)
            {
                cache.RemoveAll(entry => entry.Key == key);
                cache.Add((key, model));

                while (cache.Count > MaxCached)
                    cache.RemoveAt(0);
            }

            return model;
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
