using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshRepeat
    {
        public static MeshTopology Mirrored(this MeshTopology mesh, bool x, bool y, bool z, bool bisects, float merge)
        {
            var current = mesh;

            foreach (var (axis, on) in new[] { (0, x), (1, y), (2, z) })
            {
                if (!on)
                    continue;

                var normal = axis switch
                {
                    0 => Vector3.UnitX,
                    1 => Vector3.UnitY,
                    _ => Vector3.UnitZ,
                };

                if (bisects && current.Bisect([.. Enumerable.Range(0, current.Faces.Length)], Vector3.Zero, normal, true, false, false) is { } cut)
                    current = cut.Mesh.Carry(current);

                if (merge > 0f)
                    current = Snapped(current, normal, merge);

                if (current.Mirror(axis) is { } made)
                    current = made.Mesh;
            }

            return current;
        }

        public static MeshTopology Arrayed(this MeshTopology mesh, int count, Vector3 relative, Vector3 constant, bool merges, float distance)
        {
            if (count <= 1 || mesh.Points.Length == 0)
                return mesh;

            var min = mesh.Points.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var max = mesh.Points.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            var step = relative * (max - min) + constant;
            var result = mesh;

            for (var i = 1; i < count; i++)
                result = result.Join(mesh, Matrix4x4.CreateTranslation(step * i));

            if (!merges || distance <= 0f)
                return result;

            return result.MergeByDistance([.. Enumerable.Range(0, result.Points.Length)], distance) is { } welded ? welded.Mesh.Carry(result) : result;
        }

        private static MeshTopology Snapped(MeshTopology mesh, Vector3 normal, float merge)
        {
            var points = (Vector3[])mesh.Points.Clone();
            var moved = false;

            for (var i = 0; i < points.Length; i++)
            {
                var along = Vector3.Dot(points[i], normal);

                if (along == 0f || MathF.Abs(along) > merge)
                    continue;

                points[i] -= normal * along;
                moved = true;
            }

            return moved ? mesh.WithPoints(points) : mesh;
        }
    }
}
