using System.Text;

namespace YMM43D.Graphics.Models
{
    internal sealed class FbxNode(string name, object?[] properties)
    {
        public string Name { get; } = name;

        public object?[] Properties { get; } = properties;

        public List<FbxNode> Children { get; } = [];

        public FbxNode? Child(string name)
        {
            foreach (var child in Children)
            {
                if (child.Name == name)
                    return child;
            }

            return null;
        }

        public IEnumerable<FbxNode> ChildrenNamed(string name)
        {
            foreach (var child in Children)
            {
                if (child.Name == name)
                    yield return child;
            }
        }

        public string Text(int index)
            => index < Properties.Length && Properties[index] is byte[] bytes
                ? Encoding.UTF8.GetString(bytes)
                : string.Empty;

        public long Number(int index)
            => index < Properties.Length && Properties[index] is { } value
                ? value switch
                {
                    long number => number,
                    int number => number,
                    short number => number,
                    sbyte number => number,
                    double number => (long)number,
                    float number => (long)number,
                    _ => 0L,
                }
                : 0L;

        public double[] Doubles(int index) => index < Properties.Length && Properties[index] is double[] values ? values : [];

        public int[] Integers(int index)
            => index < Properties.Length
                ? Properties[index] switch
                {
                    int[] values => values,
                    long[] values => [.. values.Select(one => (int)one)],
                    _ => [],
                }
                : [];

        public long[] Longs(int index) => index < Properties.Length && Properties[index] is long[] values ? values : [];

        public long[] LongsOf(string name) => Child(name)?.Longs(0) ?? [];

        public double[] DoublesOf(string name) => Child(name)?.Doubles(0) ?? [];

        public int[] IntegersOf(string name) => Child(name)?.Integers(0) ?? [];

        public string TextOf(string name) => Child(name)?.Text(0) ?? string.Empty;
    }
}
