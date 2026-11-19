using System.Numerics;

namespace YMM43D.Graphics.Meshes
{
    public static class MeshShaping
    {
        private const float Least = 1e-5f;
        private const float OnPlane = 1e-4f;

        public static (MeshTopology Mesh, int[] Added)? Inset(this MeshTopology mesh, IReadOnlyCollection<int> chosenFaces, float amount)
        {
            var chosen = mesh.Existing(chosenFaces);

            if (chosen.Length == 0 || amount <= 0f)
                return null;

            var onFace = new HashSet<int>(chosen);
            var points = new List<Vector3>(mesh.Points);
            var copy = new Dictionary<int, int>();

            foreach (var f in chosen)
            {
                foreach (var corner in mesh.Faces[f])
                {
                    if (!copy.ContainsKey(corner))
                        copy[corner] = -1;
                }
            }

            var moved = Drawn(mesh, chosen, onFace, amount);

            if (moved.Count == 0)
                return null;

            foreach (var (corner, place) in moved)
            {
                copy[corner] = points.Count;
                points.Add(place);
            }

            foreach (var corner in copy.Keys.ToArray())
            {
                if (copy[corner] >= 0)
                    continue;

                copy[corner] = points.Count;
                points.Add(mesh.Points[corner]);
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

                    faces.Add([a, b, copy[b], copy[a]]);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(false);
                }
            }

            return (
                mesh.With([.. points], faces, groups, smooth),
                [.. copy.Values]);
        }

        public static (MeshTopology Mesh, int[] Added)? Bevel(this MeshTopology mesh, IReadOnlyCollection<int> chosenFaces, float amount)
        {
            var chosen = mesh.Existing(chosenFaces);

            if (chosen.Length == 0 || amount <= 0f)
                return null;

            var onFace = new HashSet<int>(chosen);
            var points = new List<Vector3>(mesh.Points);
            var inner = new Dictionary<(int Face, int Corner), int>();

            foreach (var f in chosen)
            {
                var corners = mesh.Faces[f];
                var pulled = PulledIn(mesh, f, amount);

                for (var i = 0; i < corners.Length; i++)
                {
                    inner[(f, corners[i])] = points.Count;
                    points.Add(pulled[i]);
                }
            }

            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!onFace.Contains(f))
                {
                    faces.Add(mesh.Faces[f]);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(mesh.Smooth[f]);

                    continue;
                }

                faces.Add([.. mesh.Faces[f].Select(one => inner[(f, one)])]);
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
                    var e = mesh.FindEdge(a, b);

                    if (e < 0)
                        continue;

                    var other = -1;

                    foreach (var side in mesh.EdgeFaces[e])
                    {
                        if (side != f && onFace.Contains(side))
                        {
                            other = side;

                            break;
                        }
                    }

                    if (other >= 0)
                    {
                        if (f > other)
                            continue;

                        faces.Add([inner[(f, b)], inner[(f, a)], inner[(other, a)], inner[(other, b)]]);
                    }
                    else
                    {
                        faces.Add([a, b, inner[(f, b)], inner[(f, a)]]);
                    }

                    groups.Add(mesh.Groups[f]);
                    smooth.Add(false);
                }
            }

            for (var corner = 0; corner < mesh.Points.Length; corner++)
            {
                var around = new List<int>();

                foreach (var f in chosen)
                {
                    if (mesh.Faces[f].Contains(corner))
                        around.Add(f);
                }

                if (around.Count < 3)
                    continue;

                var ring = Ring(mesh, corner, [.. around], onFace);

                if (ring.Length < 3)
                    continue;

                var cap = ring.Select(one => inner[(one, corner)]).ToArray();
                var lean = Vector3.Zero;

                foreach (var f in around)
                    lean += mesh.NormalOf(f);

                faces.Add(Facing(points, cap, lean));
                groups.Add(mesh.Groups[around[0]]);
                smooth.Add(false);
            }

            return mesh.Rebuilt(points, faces, groups, smooth, mesh.LooseEdges, mesh.IsIsolated, inner.Values);
        }

        public static (MeshTopology Mesh, int[] Added)? Solidify(this MeshTopology mesh, IReadOnlyCollection<int> chosenFaces, float thickness)
        {
            var chosen = mesh.Existing(chosenFaces);

            if (chosen.Length == 0)
                chosen = [.. Enumerable.Range(0, mesh.Faces.Length)];

            if (chosen.Length == 0 || MathF.Abs(thickness) <= Least)
                return null;

            var onFace = new HashSet<int>(chosen);
            var normals = new Dictionary<int, Vector3>();

            foreach (var f in chosen)
            {
                var normal = mesh.NormalOf(f);

                foreach (var corner in mesh.Faces[f])
                {
                    normals[corner] = normals.TryGetValue(corner, out var found) ? found + normal : normal;
                }
            }

            var points = new List<Vector3>(mesh.Points);
            var copy = new Dictionary<int, int>();

            foreach (var (corner, sum) in normals)
            {
                var lean = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : Vector3.UnitY;

                copy[corner] = points.Count;
                points.Add(mesh.Points[corner] - lean * thickness);
            }

            var faces = new List<int[]>(mesh.Faces);
            var groups = new List<int>(mesh.Groups);
            var smooth = new List<bool>(mesh.Smooth);

            foreach (var f in chosen)
            {
                faces.Add([.. mesh.Faces[f].Reverse().Select(one => copy[one])]);
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
                    var e = mesh.FindEdge(a, b);

                    if (e < 0 || mesh.EdgeFaces[e].Count(onFace.Contains) != 1)
                        continue;

                    faces.Add([b, a, copy[a], copy[b]]);
                    groups.Add(mesh.Groups[f]);
                    smooth.Add(false);
                }
            }

            return (
                mesh.With([.. points], faces, groups, smooth),
                [.. copy.Values]);
        }

        public static (MeshTopology Mesh, int[] Added)? Mirror(this MeshTopology mesh, int axis)
        {
            if (mesh.Points.Length == 0 || axis < 0 || axis > 2)
                return null;

            var points = new List<Vector3>(mesh.Points);
            var copy = new int[mesh.Points.Length];

            for (var i = 0; i < mesh.Points.Length; i++)
            {
                var place = mesh.Points[i];
                var along = At(place, axis);

                if (MathF.Abs(along) <= OnPlane)
                {
                    copy[i] = i;

                    continue;
                }

                copy[i] = points.Count;
                points.Add(Flipped(place, axis));
            }

            var faces = new List<int[]>(mesh.Faces);
            var groups = new List<int>(mesh.Groups);
            var smooth = new List<bool>(mesh.Smooth);
            var uvs = mesh.Uvs is { } source ? new List<Vector2[]>(source) : null;
            var added = new List<int>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                var corners = mesh.Faces[f];
                var made = new int[corners.Length];

                for (var i = 0; i < corners.Length; i++)
                    made[i] = copy[corners[corners.Length - 1 - i]];

                if (made.Distinct().Count() < 3)
                    continue;

                faces.Add(made);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
                uvs?.Add([.. mesh.Uvs![f].Reverse()]);
                added.AddRange(made);
            }

            var loose = new List<MeshEdge>(mesh.LooseEdges);

            foreach (var edge in mesh.LooseEdges)
            {
                loose.Add(new MeshEdge(copy[edge.A], copy[edge.B]));
                added.Add(copy[edge.A]);
                added.Add(copy[edge.B]);
            }

            if (added.Count == 0)
                return null;

            var marks = new Dictionary<MeshEdge, EdgeMark>(mesh.Marks);

            foreach (var (edge, mark) in mesh.Marks)
                marks.TryAdd(MeshTopology.Key(copy[edge.A], copy[edge.B]), mark);

            return (new MeshTopology([.. points], [.. faces], [.. groups], [.. smooth], mesh.GroupCount, loose, marks, uvs is null ? null : [.. uvs]), [.. added.Distinct()]);
        }

        private static float At(in Vector3 place, int axis) => axis == 0 ? place.X : axis == 1 ? place.Y : place.Z;

        private static Vector3 Flipped(in Vector3 place, int axis) => axis switch
        {
            0 => new Vector3(-place.X, place.Y, place.Z),
            1 => new Vector3(place.X, -place.Y, place.Z),
            _ => new Vector3(place.X, place.Y, -place.Z),
        };

        private static Dictionary<int, Vector3> Drawn(MeshTopology mesh, int[] chosen, HashSet<int> onFace, float amount)
        {
            var pulled = new Dictionary<int, List<Vector3>>();

            foreach (var f in chosen)
            {
                var corners = mesh.Faces[f];
                var normal = mesh.NormalOf(f);

                for (var i = 0; i < corners.Length; i++)
                {
                    var corner = corners[i];
                    var before = corners[(i + corners.Length - 1) % corners.Length];
                    var after = corners[(i + 1) % corners.Length];

                    var backEdge = mesh.FindEdge(before, corner);
                    var frontEdge = mesh.FindEdge(corner, after);

                    var backOpen = backEdge >= 0 && mesh.EdgeFaces[backEdge].Count(onFace.Contains) == 1;
                    var frontOpen = frontEdge >= 0 && mesh.EdgeFaces[frontEdge].Count(onFace.Contains) == 1;

                    if (!backOpen && !frontOpen)
                        continue;

                    var lean = Bisector(
                        mesh.Points[corner],
                        backOpen ? mesh.Points[before] : mesh.Points[corner],
                        frontOpen ? mesh.Points[after] : mesh.Points[corner],
                        normal,
                        backOpen,
                        frontOpen);

                    (pulled.TryGetValue(corner, out var found) ? found : pulled[corner] = [])
                        .Add(mesh.Points[corner] + lean * amount);
                }
            }

            var made = new Dictionary<int, Vector3>(pulled.Count);

            foreach (var (corner, places) in pulled)
            {
                var sum = Vector3.Zero;

                foreach (var place in places)
                    sum += place;

                made[corner] = sum / places.Count;
            }

            return made;
        }

        private static Vector3[] PulledIn(MeshTopology mesh, int face, float amount)
        {
            var corners = mesh.Faces[face];
            var normal = mesh.NormalOf(face);
            var made = new Vector3[corners.Length];

            for (var i = 0; i < corners.Length; i++)
            {
                var here = mesh.Points[corners[i]];
                var before = mesh.Points[corners[(i + corners.Length - 1) % corners.Length]];
                var after = mesh.Points[corners[(i + 1) % corners.Length]];

                made[i] = here + Bisector(here, before, after, normal, true, true) * amount;
            }

            return made;
        }

        private static Vector3 Bisector(in Vector3 here, in Vector3 before, in Vector3 after, in Vector3 normal, bool backOpen, bool frontOpen)
        {
            var back = here - before;
            var front = after - here;

            var intoBack = Inward(back, normal);
            var intoFront = Inward(front, normal);

            if (!backOpen)
                return intoFront;

            if (!frontOpen)
                return intoBack;

            var lean = intoBack + intoFront;

            if (lean.LengthSquared() <= 1e-12f)
                return intoFront;

            lean = Vector3.Normalize(lean);

            var share = Vector3.Dot(lean, intoFront);

            return share > 0.2f ? lean / share : lean;
        }

        private static Vector3 Inward(in Vector3 along, in Vector3 normal)
        {
            var side = Vector3.Cross(along, normal);

            return side.LengthSquared() > 1e-12f ? Vector3.Normalize(side) : Vector3.Zero;
        }

        private static int[] Facing(List<Vector3> points, int[] corners, in Vector3 lean)
        {
            var sum = Vector3.Zero;

            for (var i = 0; i < corners.Length; i++)
            {
                var here = points[corners[i]];
                var next = points[corners[(i + 1) % corners.Length]];

                sum += new Vector3(
                    (here.Y - next.Y) * (here.Z + next.Z),
                    (here.Z - next.Z) * (here.X + next.X),
                    (here.X - next.X) * (here.Y + next.Y));
            }

            return Vector3.Dot(-sum, lean) >= 0f ? corners : [.. Enumerable.Reverse(corners)];
        }

        private static int[] Ring(MeshTopology mesh, int corner, int[] around, HashSet<int> onFace)
        {
            var order = new List<int> { around[0] };
            var left = new HashSet<int>(around.Skip(1));

            while (left.Count > 0)
            {
                var last = order[^1];
                var next = -1;

                foreach (var edge in mesh.VertexEdges[corner])
                {
                    var sides = mesh.EdgeFaces[edge];

                    if (!sides.Contains(last))
                        continue;

                    foreach (var side in sides)
                    {
                        if (side != last && left.Contains(side) && onFace.Contains(side))
                        {
                            next = side;

                            break;
                        }
                    }

                    if (next >= 0)
                        break;
                }

                if (next < 0)
                    return [];

                order.Add(next);
                left.Remove(next);
            }

            var normal = Vector3.Zero;

            foreach (var face in order)
                normal += mesh.NormalOf(face);

            if (normal.LengthSquared() <= 1e-12f)
                return [.. order];

            normal = Vector3.Normalize(normal);

            var first = mesh.NormalOf(order[0]);
            var second = mesh.NormalOf(order[1 % order.Count]);

            return Vector3.Dot(Vector3.Cross(first, second), normal) >= 0f
                ? [.. order]
                : [.. Enumerable.Reverse(order)];
        }
    }
}
