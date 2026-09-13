using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace YMM43D.Graphics
{
    public static partial class ShaderLibrary
    {
        private const string Folder = ".Shaders.";

        private static readonly ConcurrentDictionary<(string Assembly, string Name), string> sources = new();

        private static readonly ConcurrentDictionary<(string Assembly, string Name, string EntryPoint, string Profile), Lazy<byte[]>> bytecodes = new();

        private static readonly Assembly Core = typeof(ShaderLibrary).Assembly;

        public static string Load(Assembly assembly, string name)
            => sources.GetOrAdd((assembly.FullName ?? string.Empty, name), _ => Expand(assembly, name, []));

        public static byte[] Compile(Assembly assembly, string name, string entryPoint, string profile)
        {
            var key = (assembly.FullName ?? string.Empty, name, entryPoint, profile);
            var compiled = bytecodes.GetOrAdd(key, _ => new Lazy<byte[]>(
                () => ShaderCompiler.Compile(Load(assembly, name), entryPoint, profile, name)));

            try
            {
                return compiled.Value;
            }
            catch
            {
                bytecodes.TryRemove(new KeyValuePair<(string, string, string, string), Lazy<byte[]>>(key, compiled));
                throw;
            }
        }

        private static string Expand(Assembly assembly, string path, HashSet<string> taken)
        {
            var name = Path.GetFileName(path);

            if (!taken.Add(name))
                return string.Empty;

            var builder = new StringBuilder();

            foreach (var line in Read(assembly, name).Split('\n'))
            {
                var include = IncludePattern().Match(line);

                if (include.Success)
                    builder.Append(Expand(assembly, include.Groups[1].Value, taken));
                else
                    builder.Append(line.TrimEnd('\r')).Append('\n');
            }

            return builder.ToString();
        }

        private static string Read(Assembly assembly, string name)
        {
            using var stream = Open(assembly, name) ?? Open(Core, name)
                ?? throw new InvalidOperationException(
                    $"シェーダー {name} が {assembly.GetName().Name} にも YMM43D にも入っていません。"
                    + "csproj の EmbeddedResource を確かめてください。");

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static Stream? Open(Assembly assembly, string path)
        {
            var suffix = Folder + Path.GetFileName(path);

            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (resource.EndsWith(suffix, StringComparison.Ordinal))
                    return assembly.GetManifestResourceStream(resource);
            }

            return null;
        }

        [GeneratedRegex("""^\s*#include\s*"([^"]+)"\s*$""")]
        private static partial Regex IncludePattern();
    }
}
