namespace YMM43D.Graphics.Meshes
{
    public static class MeshDelete
    {
        public static MeshTopology? DeleteVertices(this MeshTopology mesh, IReadOnlyCollection<int> vertices)
        {
            var gone = new HashSet<int>(mesh.ExistingPoints(vertices));

            if (gone.Count == 0 || gone.Count == mesh.Points.Length)
                return null;

            bool Apart(MeshEdge edge) => !gone.Contains(edge.A) && !gone.Contains(edge.B);

            return Keep(mesh, face => !mesh.Faces[face].Any(gone.Contains), Apart, Apart, vertex => !gone.Contains(vertex));
        }

        public static MeshTopology? DeleteEdges(this MeshTopology mesh, IReadOnlyCollection<int> edges)
        {
            var gone = new HashSet<int>(mesh.ExistingEdges(edges));

            if (gone.Count == 0)
                return null;

            var touched = new HashSet<int>(gone.SelectMany(edge => new[] { mesh.Edges[edge].A, mesh.Edges[edge].B }));

            bool Kept(MeshEdge edge) => !gone.Contains(mesh.FindEdge(edge.A, edge.B));

            return Keep(mesh, face => !mesh.FaceEdges[face].Any(gone.Contains), Kept, Kept, vertex => !touched.Contains(vertex));
        }

        public static MeshTopology? DeleteFaces(this MeshTopology mesh, IReadOnlyCollection<int> faces)
        {
            var gone = new HashSet<int>(mesh.Existing(faces));

            if (gone.Count == 0)
                return null;

            return Keep(mesh, face => !gone.Contains(face), _ => true, _ => false, mesh.IsIsolated);
        }

        public static MeshTopology? DeleteOnlyFaces(this MeshTopology mesh, IReadOnlyCollection<int> faces)
        {
            var gone = new HashSet<int>(mesh.Existing(faces));

            if (gone.Count == 0)
                return null;

            return Keep(mesh, face => !gone.Contains(face), _ => true, _ => true, _ => true);
        }

        public static MeshTopology? DeleteOnlyEdgesAndFaces(this MeshTopology mesh, IReadOnlyCollection<int> edges)
        {
            var gone = new HashSet<int>(mesh.ExistingEdges(edges));

            if (gone.Count == 0)
                return null;

            bool Kept(MeshEdge edge) => !gone.Contains(mesh.FindEdge(edge.A, edge.B));

            return Keep(mesh, face => !mesh.FaceEdges[face].Any(gone.Contains), Kept, Kept, _ => true);
        }

        internal static MeshTopology? Keep(MeshTopology mesh, Func<int, bool> keepsFace, Func<MeshEdge, bool> keepsLoose, Func<MeshEdge, bool> keepsLeftOver, Func<int, bool> keepsVertex)
        {
            var faces = new List<int[]>();
            var groups = new List<int>();
            var smooth = new List<bool>();
            var dropped = new List<int>();

            for (var f = 0; f < mesh.Faces.Length; f++)
            {
                if (!keepsFace(f))
                {
                    dropped.Add(f);
                    continue;
                }

                faces.Add(mesh.Faces[f]);
                groups.Add(mesh.Groups[f]);
                smooth.Add(mesh.Smooth[f]);
            }

            var loose = new List<MeshEdge>();

            foreach (var edge in mesh.LooseEdges)
            {
                if (keepsLoose(edge))
                    loose.Add(edge);
            }

            foreach (var f in dropped)
            {
                foreach (var e in mesh.FaceEdges[f])
                {
                    var edge = mesh.Edges[e];

                    if (keepsLeftOver(edge))
                        loose.Add(edge);
                }
            }

            var (built, _) = MeshPacking.Pack(mesh.Points, faces, groups, smooth, loose, keepsVertex, mesh.GroupCount);

            if (built.Points.Length == 0)
                return null;

            return built.Points.Length == mesh.Points.Length && built.Faces.Length == mesh.Faces.Length && built.Edges.Length == mesh.Edges.Length
                ? null
                : built;
        }
    }
}
