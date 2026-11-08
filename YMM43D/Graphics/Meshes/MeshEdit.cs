using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshEdit
    {
        public const int MaxFaceCorners = 8;

        public static int[] FacesIn(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var found = new List<int>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (mesh.Faces[f].Length > 0 && mesh.Faces[f].All(selection.Contains))
                    found.Add(f);
            }

            return [.. found];
        }

        public static (MeshTopology Mesh, int[] Added)? Extrude(
            this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var chosen = mesh.FacesIn(selection);

            if (chosen.Length == 0)
                return null;

            var onFace = new HashSet<int>(chosen);
            var points = new List<Vector3>(mesh.Points);
            var copy = new Dictionary<int, int>();

            foreach (var f in chosen)
            {
                foreach (var corner in mesh.Faces[f])
                {
                    if (copy.ContainsKey(corner))
                        continue;

                    copy[corner] = points.Count;
                    points.Add(mesh.Points[corner]);
                }
            }

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                faces.Add(onFace.Contains(f) ? [.. mesh.Faces[f].Select(one => copy[one])] : mesh.Faces[f]);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
            }

            foreach (var f in chosen)
            {
                var corners = mesh.Faces[f];

                for (var i = 0; i < corners.Length; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % corners.Length];
                    var edge = mesh.FindEdge(a, b);

                    if (edge < 0 || mesh.EdgeFaces[edge].Count(onFace.Contains) != 1)
                        continue;

                    faces.Add([a, b, copy[b], copy[a]]);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(false);
                }
            }

            return (
                new MeshTopology([.. points], [.. faces], [.. groups], [.. smooth], mesh.GroupCount),
                [.. copy.Values]);
        }

        public static MeshTopology? Delete(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            if (selection.Count == 0)
                return null;

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (mesh.Faces[f].Any(selection.Contains))
                    continue;

                faces.Add(mesh.Faces[f]);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
            }

            return faces.Count == mesh.Faces.Length || faces.Count == 0
                ? null
                : Compact(mesh, points: mesh.Points, faces, groups, smooth);
        }

        public static MeshTopology? MakeFace(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            if (selection.Count is < 3 or > MaxFaceCorners)
                return null;

            var chosen = selection.Where(one => one >= 0 && one < mesh.Points.Length).ToArray();

            if (chosen.Length != selection.Count)
                return null;

            foreach (var face in mesh.Faces)
            {
                if (face.Length == chosen.Length && face.All(selection.Contains))
                    return null;
            }

            var ordered = Around(mesh, chosen);

            if (ordered is null)
                return null;

            var faces = new List<int[]>(mesh.Faces) { ordered };
            var groups = new List<int>(mesh.Groups) { 0 };
            var smooth = new List<bool>(mesh.Smooth) { false };

            return new MeshTopology(
                mesh.Points, [.. faces], [.. groups], [.. smooth], mesh.GroupCount);
        }

        private static int[]? Around(MeshTopology mesh, int[] chosen)
        {
            var middle = Vector3.Zero;

            foreach (var one in chosen)
                middle += mesh.Points[one];

            middle /= chosen.Length;

            var normal = Vector3.Zero;

            for (var i = 0; i < chosen.Length; i++)
            {
                for (var j = i + 1; j < chosen.Length; j++)
                {
                    for (var k = j + 1; k < chosen.Length; k++)
                    {
                        var turn = Vector3.Cross(
                            mesh.Points[chosen[j]] - mesh.Points[chosen[i]],
                            mesh.Points[chosen[k]] - mesh.Points[chosen[i]]);

                        if (turn.LengthSquared() > normal.LengthSquared())
                            normal = turn;
                    }
                }
            }

            if (normal.LengthSquared() < 1e-14f)
                return null;

            normal = Vector3.Normalize(normal);

            var outward = middle - mesh.Middle([.. Enumerable.Range(0, mesh.Points.Length)]);

            if (Vector3.Dot(normal, outward) < 0f)
                normal = -normal;

            var (right, up) = TurnPlane(normal);

            return
            [
                .. chosen
                    .Select(one => (Index: one, Angle: Angle(mesh.Points[one] - middle, right, up)))
                    .OrderByDescending(one => one.Angle)
                    .Select(one => one.Index),
            ];
        }

        private static (Vector3 Right, Vector3 Up) TurnPlane(in Vector3 normal)
        {
            var guess = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            var right = Vector3.Cross(guess, normal);

            right = right.LengthSquared() > 1e-12f ? Vector3.Normalize(right) : Vector3.UnitX;

            return (right, Vector3.Cross(normal, right));
        }

        private static float Angle(in Vector3 offset, in Vector3 right, in Vector3 up)
            => MathF.Atan2(Vector3.Dot(offset, up), Vector3.Dot(offset, right));

        public static (MeshTopology Mesh, int[] Added)? Merge(
            this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            if (selection.Count < 2)
                return null;

            var middle = mesh.Middle([.. selection]);
            var points = new List<Vector3>(mesh.Points) { middle };
            var made = points.Count - 1;

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = new List<int>();

                foreach (var corner in mesh.Faces[f])
                {
                    var moved = selection.Contains(corner) ? made : corner;

                    if (corners.Count == 0 || corners[^1] != moved)
                        corners.Add(moved);
                }

                while (corners.Count > 1 && corners[0] == corners[^1])
                    corners.RemoveAt(corners.Count - 1);

                if (corners.Count < 3)
                    continue;

                faces.Add([.. corners]);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
            }

            if (faces.Count == 0)
                return null;

            var built = Compact(mesh, [.. points], faces, groups, smooth);

            return (built, [.. Enumerable.Range(0, built.Points.Length)
                .Where(one => Vector3.Distance(built.Points[one], middle) < 1e-6f)]);
        }

        public static MeshTopology Subdivide(this MeshTopology mesh)
        {
            var points = new List<Vector3>(mesh.Points);
            var middles = new int[mesh.Edges.Length];

            for (var e = 0; e < mesh.Edges.Length; e++)
            {
                points.Add((mesh.Points[mesh.Edges[e].A] + mesh.Points[mesh.Edges[e].B]) / 2f);
                middles[e] = points.Count - 1;
            }

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = mesh.Faces[f];
                var edges = mesh.FaceEdges[f];
                var centre = Vector3.Zero;

                foreach (var corner in corners)
                    centre += mesh.Points[corner];

                points.Add(centre / corners.Length);

                var middle = points.Count - 1;

                for (var i = 0; i < corners.Length; i++)
                {
                    var before = middles[edges[(i + corners.Length - 1) % corners.Length]];
                    var after = middles[edges[i]];

                    faces.Add([corners[i], after, middle, before]);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(mesh.Smooth[f]);
                }
            }

            return new MeshTopology(
                [.. points], [.. faces], [.. groups], [.. smooth], mesh.GroupCount);
        }

        public static int[] LinkedTo(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var reached = new HashSet<int>(selection);
            var queue = new Queue<int>(selection);

            while (queue.Count > 0)
            {
                var at = queue.Dequeue();

                if (at < 0 || at >= mesh.VertexEdges.Length)
                    continue;

                foreach (var edge in mesh.VertexEdges[at])
                {
                    var next = mesh.Edges[edge].Other(at);

                    if (reached.Add(next))
                        queue.Enqueue(next);
                }
            }

            return [.. reached];
        }

        public static int[] Grown(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var found = new HashSet<int>(selection);

            foreach (var at in selection)
            {
                if (at < 0 || at >= mesh.VertexEdges.Length)
                    continue;

                foreach (var edge in mesh.VertexEdges[at])
                    found.Add(mesh.Edges[edge].Other(at));
            }

            return [.. found];
        }

        public static int[] Shrunk(this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var found = new List<int>();

            foreach (var at in selection)
            {
                if (at < 0 || at >= mesh.VertexEdges.Length)
                    continue;

                var inside = true;

                foreach (var edge in mesh.VertexEdges[at])
                {
                    if (!selection.Contains(mesh.Edges[edge].Other(at)))
                        inside = false;
                }

                if (inside)
                    found.Add(at);
            }

            return found.Count == 0 ? [.. selection] : [.. found];
        }

        public static (int Forward, int Backward)[] SlideRails(
            this MeshTopology mesh, IReadOnlySet<int> selection)
        {
            var rails = new (int, int)[mesh.Points.Length];

            for (var i = 0; i < rails.Length; i++)
                rails[i] = (-1, -1);

            foreach (var at in selection)
            {
                if (at < 0 || at >= mesh.VertexEdges.Length)
                    continue;

                var free = new List<int>();

                foreach (var edge in mesh.VertexEdges[at])
                {
                    var other = mesh.Edges[edge].Other(at);

                    if (!selection.Contains(other))
                        free.Add(other);
                }

                rails[at] = free.Count switch
                {
                    1 => (free[0], free[0]),
                    2 => (free[0], free[1]),
                    _ => (-1, -1),
                };
            }

            return rails;
        }

        private static MeshTopology Compact(
            MeshTopology mesh,
            IReadOnlyList<Vector3> points,
            List<int[]> faces,
            List<int> groups,
            List<bool> smooth)
        {
            var moved = new int[points.Count];

            Array.Fill(moved, -1);

            var kept = new List<Vector3>();

            foreach (var face in faces)
            {
                foreach (var corner in face)
                {
                    if (moved[corner] >= 0)
                        continue;

                    moved[corner] = kept.Count;
                    kept.Add(points[corner]);
                }
            }

            var built = new int[faces.Count][];

            for (var f = 0; f < faces.Count; f++)
                built[f] = [.. faces[f].Select(one => moved[one])];

            return new MeshTopology(
                [.. kept], built, [.. groups], [.. smooth], mesh.GroupCount);
        }
    }
}
