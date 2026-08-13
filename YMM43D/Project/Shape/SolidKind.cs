using System.ComponentModel.DataAnnotations;
using YMM43D.Graphics.Meshes;

namespace YMM43D.Project.Shape
{
    public enum SolidKind
    {
        [Display(Name = "平面", Description = "1枚の四角形")]
        Plane,

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

        [Display(Name = "球", Description = "分割の細かさで滑らかさが決まります")]
        Sphere,

        [Display(Name = "円柱", Description = "色は 側面・上面・底面 の順です")]
        Cylinder,

        [Display(Name = "円錐", Description = "色は 側面・底面 の順です")]
        Cone,

        [Display(Name = "ドーナツ", Description = "太さで穴の大きさが決まります")]
        Torus,
    }

    public static class Solids
    {
        public const int DefaultSegments = 32;

        public const int DefaultThickness = 30;

        private const int MaxCached = 64;

        private static readonly Lock gate = new();

        private static readonly Dictionary<(SolidKind, int, int), SurfaceGeometry> cache = [];

        public static bool IsCurved(SolidKind kind)
            => kind is SolidKind.Sphere or SolidKind.Cylinder or SolidKind.Cone or SolidKind.Torus;

        public static int GroupCountOf(SolidKind kind) => kind switch
        {
            SolidKind.Plane => 1,
            SolidKind.Tetrahedron => 4,
            SolidKind.Cube => 6,
            SolidKind.Octahedron => 8,
            SolidKind.Dodecahedron => 12,
            SolidKind.Icosahedron => 20,
            SolidKind.Sphere => 1,
            SolidKind.Cylinder => 3,
            SolidKind.Cone => 2,
            _ => 1,
        };

        public static SurfaceGeometry Get(SolidKind kind, int segments, int thickness)
        {
            var key = Key(kind, segments, thickness);

            lock (gate)
            {
                if (cache.TryGetValue(key, out var found))
                    return found;

                if (cache.Count >= MaxCached)
                    cache.Clear();

                return cache[key] = Create(key.Item1, key.Item2, key.Item3);
            }
        }

        public static SurfaceGeometry Create(SolidKind kind, int segments, int thickness) => kind switch
        {
            SolidKind.Plane => Primitives.Plane(),
            SolidKind.Tetrahedron => Primitives.Tetrahedron(),
            SolidKind.Cube => Primitives.Cube(),
            SolidKind.Octahedron => Primitives.Octahedron(),
            SolidKind.Dodecahedron => Primitives.Dodecahedron(),
            SolidKind.Icosahedron => Primitives.Icosahedron(),
            SolidKind.Sphere => Primitives.Sphere(segments),
            SolidKind.Cylinder => Primitives.Cylinder(segments),
            SolidKind.Cone => Primitives.Cone(segments),
            _ => Primitives.Torus(segments, thickness / 100f),
        };

        private static (SolidKind, int, int) Key(SolidKind kind, int segments, int thickness)
            => IsCurved(kind)
                ? (kind, segments, kind == SolidKind.Torus ? thickness : 0)
                : (kind, 0, 0);
    }
}
