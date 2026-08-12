using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace Shape3D
{
    public enum SolidKind
    {
        [Display(Name = "四面体", Description = "正三角形4枚")]
        Tetrahedron,

        [Display(Name = "六面体", Description = "正方形6枚（立方体）")]
        Cube,

        [Display(Name = "八面体", Description = "正三角形8枚")]
        Octahedron,

        [Display(Name = "十二面体", Description = "正五角形12枚")]
        Dodecahedron,

        [Display(Name = "二十面体", Description = "正三角形20枚")]
        Icosahedron,
    }

    /// <summary>
    /// 正多面体の頂点と面。
    /// </summary>
    /// <remarks>
    /// 面の頂点は「外から見て」の並び順で書いていません。書き下すと符号を間違えて
    /// 面が内側を向くので、<see cref="Create"/> が中心からの向きで判定して直します。
    /// </remarks>
    internal sealed record Polyhedron(Vector3[] Vertices, int[][] Faces)
    {
        // 形は種類ごとに1つあれば足りる。毎フレーム組み直さないよう取っておく。
        // 取り出したものは読むだけにすること。
        private static readonly ConcurrentDictionary<SolidKind, Polyhedron> cache = new();

        public static Polyhedron Get(SolidKind kind) => cache.GetOrAdd(kind, Create);

        public static int FaceCountOf(SolidKind kind) => kind switch
        {
            SolidKind.Tetrahedron => 4,
            SolidKind.Cube => 6,
            SolidKind.Octahedron => 8,
            SolidKind.Dodecahedron => 12,
            _ => 20,
        };

        public static Polyhedron Create(SolidKind kind)
        {
            var (vertices, faces) = kind switch
            {
                SolidKind.Tetrahedron => Tetrahedron(),
                SolidKind.Cube => Cube(),
                SolidKind.Octahedron => Octahedron(),
                SolidKind.Dodecahedron => Dodecahedron(),
                _ => Icosahedron(),
            };

            Normalize(vertices);

            foreach (var face in faces)
                FaceOutward(vertices, face);

            return new Polyhedron(vertices, faces);
        }

        /// <summary>
        /// いちばん長い差し渡しが 1 になるように縮めます。
        /// </summary>
        /// <remarks>
        /// 「サイズ」を差し渡しとして扱えるようにするためです。立方体では一辺と
        /// 同じ意味になるので、多面体を切り替えても大きさの感覚が揃います。
        /// </remarks>
        private static void Normalize(Vector3[] vertices)
        {
            var extent = 0f;
            foreach (var vertex in vertices)
                extent = MathF.Max(extent, MathF.Max(MathF.Abs(vertex.X), MathF.Max(MathF.Abs(vertex.Y), MathF.Abs(vertex.Z))));

            if (extent <= 0f)
                return;

            var scale = 0.5f / extent;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] *= scale;
        }

        /// <summary>
        /// 面が外を向くように、必要なら頂点の並びを裏返します。
        /// </summary>
        /// <remarks>
        /// 正多面体は原点が中心なので、面の重心がそのまま外向きになります。
        /// 面の法線が重心と逆を向いていたら、その面は内側を向いています。
        /// </remarks>
        private static void FaceOutward(Vector3[] vertices, int[] face)
        {
            var centroid = Vector3.Zero;
            foreach (var index in face)
                centroid += vertices[index];
            centroid /= face.Length;

            var normal = Vector3.Cross(
                vertices[face[1]] - vertices[face[0]],
                vertices[face[2]] - vertices[face[0]]);

            // D3D は既定で時計回りを表とする。左手回りに見える並びが、右手系の
            // 外積では内向きに出るので、外を向かせるには外積が重心と逆であること。
            if (Vector3.Dot(normal, centroid) < 0f)
                return;

            Array.Reverse(face);
        }

        private static (Vector3[], int[][]) Tetrahedron() => (
            [
                new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1),
            ],
            [
                [0, 1, 2], [0, 3, 1], [0, 2, 3], [1, 3, 2],
            ]);

        private static (Vector3[], int[][]) Cube() => (
            [
                new(-1, 1, 1), new(1, 1, 1), new(1, -1, 1), new(-1, -1, 1),
                new(-1, 1, -1), new(1, 1, -1), new(1, -1, -1), new(-1, -1, -1),
            ],
            [
                [0, 1, 2, 3], // 前面 (+Z)
                [5, 4, 7, 6], // 背面 (-Z)
                [4, 0, 3, 7], // 左面 (-X)
                [1, 5, 6, 2], // 右面 (+X)
                [4, 5, 1, 0], // 上面 (+Y)
                [3, 2, 6, 7], // 下面 (-Y)
            ]);

        private static (Vector3[], int[][]) Octahedron() => (
            [
                new(1, 0, 0), new(-1, 0, 0),
                new(0, 1, 0), new(0, -1, 0),
                new(0, 0, 1), new(0, 0, -1),
            ],
            [
                [0, 2, 4], [2, 1, 4], [1, 3, 4], [3, 0, 4],
                [2, 0, 5], [1, 2, 5], [3, 1, 5], [0, 3, 5],
            ]);

        private static (Vector3[], int[][]) Icosahedron()
        {
            var phi = (1f + MathF.Sqrt(5f)) / 2f;

            return (
                [
                    new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0),
                    new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi),
                    new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1),
                ],
                [
                    [0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11],
                    [1, 5, 9], [5, 11, 4], [11, 10, 2], [10, 7, 6], [7, 1, 8],
                    [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9],
                    [4, 9, 5], [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1],
                ]);
        }

        private static (Vector3[], int[][]) Dodecahedron()
        {
            var phi = (1f + MathF.Sqrt(5f)) / 2f;
            var inv = 1f / phi;

            return (
                [
                    new(1, 1, 1), new(1, 1, -1), new(1, -1, 1), new(1, -1, -1),
                    new(-1, 1, 1), new(-1, 1, -1), new(-1, -1, 1), new(-1, -1, -1),
                    new(0, inv, phi), new(0, inv, -phi), new(0, -inv, phi), new(0, -inv, -phi),
                    new(inv, phi, 0), new(inv, -phi, 0), new(-inv, phi, 0), new(-inv, -phi, 0),
                    new(phi, 0, inv), new(phi, 0, -inv), new(-phi, 0, inv), new(-phi, 0, -inv),
                ],
                [
                    [0, 8, 10, 2, 16],
                    [0, 16, 17, 1, 12],
                    [0, 12, 14, 4, 8],
                    [8, 4, 18, 6, 10],
                    [10, 6, 15, 13, 2],
                    [2, 13, 3, 17, 16],
                    [1, 17, 3, 11, 9],
                    [1, 9, 5, 14, 12],
                    [14, 5, 19, 18, 4],
                    [18, 19, 7, 15, 6],
                    [15, 7, 11, 3, 13],
                    [9, 11, 7, 19, 5],
                ]);
        }
    }
}
