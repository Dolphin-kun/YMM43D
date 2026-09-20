using System.IO;
using System.IO.Compression;
using System.Text;

namespace YMM43D.Graphics.Models
{
    // FBX のファイルは、名前と値と子を持つ入れ子の記録でできている。
    // ここではその形をそのまま読み取るだけで、中身の意味づけは FbxModelLoader が行う。
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

        public double[] Doubles(int index)
            => index < Properties.Length && Properties[index] is double[] values ? values : [];

        public int[] Integers(int index)
            => index < Properties.Length && Properties[index] is int[] values ? values : [];

        // 子の名前で数値の並びを取り出す。無ければ空。
        public double[] DoublesOf(string name) => Child(name)?.Doubles(0) ?? [];

        public int[] IntegersOf(string name) => Child(name)?.Integers(0) ?? [];

        public string TextOf(string name) => Child(name)?.Text(0) ?? string.Empty;
    }

    internal static class FbxDocument
    {
        private const string BinaryMagic = "Kaydara FBX Binary  ";

        private const int MaxDepth = 64;

        public static FbxNode Load(string path)
        {
            var bytes = File.ReadAllBytes(path);

            if (bytes.Length < 27 || Encoding.ASCII.GetString(bytes, 0, BinaryMagic.Length) != BinaryMagic)
            {
                throw new NotSupportedException(
                    "テキスト形式の fbx には対応していません。バイナリ形式で書き出し直してください。");
            }

            var version = BitConverter.ToUInt32(bytes, 23);
            var wide = version >= 7500;
            var root = new FbxNode("", []);

            var at = 27;

            while (ReadNode(bytes, ref at, wide, 0) is { } node)
                root.Children.Add(node);

            return root;
        }

        private static FbxNode? ReadNode(byte[] bytes, ref int at, bool wide, int depth)
        {
            if (depth > MaxDepth)
                throw new InvalidDataException("fbx の入れ子が深すぎます。");

            var headerSize = wide ? 25 : 13;

            if (at + headerSize > bytes.Length)
                return null;

            long end, propertyCount, propertyLength;

            if (wide)
            {
                end = BitConverter.ToInt64(bytes, at);
                propertyCount = BitConverter.ToInt64(bytes, at + 8);
                propertyLength = BitConverter.ToInt64(bytes, at + 16);
                at += 24;
            }
            else
            {
                end = BitConverter.ToUInt32(bytes, at);
                propertyCount = BitConverter.ToUInt32(bytes, at + 4);
                propertyLength = BitConverter.ToUInt32(bytes, at + 8);
                at += 12;
            }

            var nameLength = bytes[at];
            at += 1;

            // すべて 0 の記録は、並びの終わりを表す。
            if (end == 0 && propertyCount == 0 && propertyLength == 0 && nameLength == 0)
                return null;

            if (end > bytes.Length)
                throw new InvalidDataException("fbx の記録がファイルの終わりを越えています。");

            var name = Encoding.UTF8.GetString(bytes, at, nameLength);
            at += nameLength;

            var properties = new object?[propertyCount];

            for (var i = 0; i < propertyCount; i++)
                properties[i] = ReadProperty(bytes, ref at);

            var node = new FbxNode(name, properties);

            while (at < end - (headerSize - 1))
            {
                if (ReadNode(bytes, ref at, wide, depth + 1) is not { } child)
                    break;

                node.Children.Add(child);
            }

            at = (int)end;
            return node;
        }

        private static object? ReadProperty(byte[] bytes, ref int at)
        {
            var type = (char)bytes[at];
            at += 1;

            switch (type)
            {
                case 'Y': return Take<short>(bytes, ref at, 2, BitConverter.ToInt16);
                case 'C': return bytes[at++] != 0;
                case 'I': return Take<int>(bytes, ref at, 4, BitConverter.ToInt32);
                case 'F': return Take<float>(bytes, ref at, 4, BitConverter.ToSingle);
                case 'D': return Take<double>(bytes, ref at, 8, BitConverter.ToDouble);
                case 'L': return Take<long>(bytes, ref at, 8, BitConverter.ToInt64);

                case 'S':
                case 'R':
                    {
                        var length = BitConverter.ToInt32(bytes, at);
                        at += 4;
                        var value = bytes[at..(at + length)];
                        at += length;
                        return value;
                    }

                case 'f': return ToDoubles(ReadArray(bytes, ref at, 4), 4);
                case 'd': return ToDoubles(ReadArray(bytes, ref at, 8), 8);
                case 'i': return ToIntegers(ReadArray(bytes, ref at, 4), 4);
                case 'l': return ToIntegers(ReadArray(bytes, ref at, 8), 8);
                case 'b': return ToIntegers(ReadArray(bytes, ref at, 1), 1);

                default:
                    throw new InvalidDataException($"fbx の値の型 {type} には対応していません。");
            }
        }

        private static T Take<T>(byte[] bytes, ref int at, int size, Func<byte[], int, T> read)
        {
            var value = read(bytes, at);
            at += size;
            return value;
        }

        private static byte[] ReadArray(byte[] bytes, ref int at, int elementSize)
        {
            var count = BitConverter.ToInt32(bytes, at);
            var encoding = BitConverter.ToInt32(bytes, at + 4);
            var compressedLength = BitConverter.ToInt32(bytes, at + 8);
            at += 12;

            var raw = bytes[at..(at + compressedLength)];
            at += compressedLength;

            if (encoding == 0)
                return raw;

            using var source = new MemoryStream(raw);
            using var inflate = new ZLibStream(source, CompressionMode.Decompress);
            using var target = new MemoryStream(count * elementSize);

            inflate.CopyTo(target);

            return target.ToArray();
        }

        private static double[] ToDoubles(byte[] data, int elementSize)
        {
            var count = data.Length / elementSize;
            var values = new double[count];

            for (var i = 0; i < count; i++)
            {
                values[i] = elementSize == 4
                    ? BitConverter.ToSingle(data, i * 4)
                    : BitConverter.ToDouble(data, i * 8);
            }

            return values;
        }

        private static int[] ToIntegers(byte[] data, int elementSize)
        {
            var count = data.Length / elementSize;
            var values = new int[count];

            for (var i = 0; i < count; i++)
            {
                values[i] = elementSize switch
                {
                    1 => data[i],
                    4 => BitConverter.ToInt32(data, i * 4),
                    _ => (int)BitConverter.ToInt64(data, i * 8),
                };
            }

            return values;
        }
    }
}
