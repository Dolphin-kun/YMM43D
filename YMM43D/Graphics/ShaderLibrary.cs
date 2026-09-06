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

        private static readonly ConcurrentDictionary<(string Assembly, string Name), string> cache = new();

        private static readonly Assembly Core = typeof(ShaderLibrary).Assembly;

        public static string Load(Assembly assembly, string name)
            => cache.GetOrAdd((assembly.FullName ?? string.Empty, name), _ => Expand(assembly, name, []));

        public static byte[] Compile(Assembly assembly, string name, string entryPoint, string profile)
            => ShaderCompiler.Compile(Load(assembly, name), entryPoint, profile, name);

        private static string Expand(Assembly assembly, string name, HashSet<string> taken)
        {
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

        private static Stream? Open(Assembly assembly, string name)
        {
            var suffix = Folder + name;

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
