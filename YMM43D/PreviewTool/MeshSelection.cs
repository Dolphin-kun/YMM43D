using System.Numerics;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool
{
    internal sealed class MeshSelection
    {
        private const float Weld = 1e-4f;

        private readonly HashSet<int> vertices = [];
        private readonly HashSet<int> edges = [];
        private readonly HashSet<int> faces = [];
        private readonly HashSet<int> hiddenVertices = [];
        private readonly HashSet<int> hiddenEdges = [];
        private readonly HashSet<int> hiddenFaces = [];

        private MeshTopology? mesh;

        public IReadOnlySet<int> Vertices => vertices;

        public IReadOnlySet<int> Edges => edges;

        public IReadOnlySet<int> Faces => faces;

        public IReadOnlySet<int> HiddenVertices => hiddenVertices;

        public IReadOnlySet<int> HiddenEdges => hiddenEdges;

        public IReadOnlySet<int> HiddenFaces => hiddenFaces;

        public MeshPart Part { get; private set; } = MeshPart.Vertices;

        public int Active { get; private set; } = -1;

        public long Revision { get; private set; }

        public long HiddenRevision { get; private set; }

        public bool IsEmpty => vertices.Count == 0;

        public bool HasHidden => hiddenVertices.Count > 0 || hiddenEdges.Count > 0 || hiddenFaces.Count > 0;

        public MeshSelection Copy()
        {
            var copy = new MeshSelection
            {
                mesh = mesh,
                Part = Part,
                Active = Active,
                Revision = Revision,
                HiddenRevision = HiddenRevision,
            };

            copy.vertices.UnionWith(vertices);
            copy.edges.UnionWith(edges);
            copy.faces.UnionWith(faces);
            copy.hiddenVertices.UnionWith(hiddenVertices);
            copy.hiddenEdges.UnionWith(hiddenEdges);
            copy.hiddenFaces.UnionWith(hiddenFaces);

            return copy;
        }

        public void Bind(MeshTopology current)
        {
            if (ReferenceEquals(mesh, current))
                return;

            var previous = mesh;

            mesh = current;

            if (previous is null
                || (ReferenceEquals(previous.Faces, current.Faces) && previous.Points.Length == current.Points.Length))
            {
                Trim(current);
                return;
            }

            Remap(previous, current);
        }

        public void Replace(MeshTopology made, IEnumerable<int> chosen)
        {
            var previous = mesh;

            mesh = made;

            if (previous is not null)
                RemapHidden(previous, made);

            vertices.Clear();

            foreach (var one in chosen)
            {
                if (one >= 0 && one < made.Points.Length && !hiddenVertices.Contains(one))
                    vertices.Add(one);
            }

            Active = -1;
            Lift();
        }

        public void SetPart(MeshPart wanted)
        {
            if (Part == wanted)
                return;

            Part = wanted;
            Active = -1;
            Lift();
        }

        public void Apply(IEnumerable<int> elements, SelectOp op, int active = -1)
        {
            if (mesh is not { } current)
                return;

            var target = PartSet();
            var wanted = elements.Where(one => IsShown(current, one)).Distinct().ToArray();

            switch (op)
            {
                case SelectOp.Set:
                    target.Clear();
                    target.UnionWith(wanted);
                    break;

                case SelectOp.Add:
                    target.UnionWith(wanted);
                    break;

                case SelectOp.Subtract:
                    target.ExceptWith(wanted);
                    break;

                default:
                    if (wanted.Length > 0 && wanted.All(target.Contains))
                    {
                        target.ExceptWith(wanted);

                        if (wanted.Contains(Active))
                            Active = -1;

                        Flush();
                        return;
                    }

                    target.UnionWith(wanted);
                    break;
            }

            if (op is SelectOp.Subtract)
            {
                if (wanted.Contains(Active))
                    Active = -1;
            }
            else if (active >= 0 && target.Contains(active))
            {
                Active = active;
            }
            else if (op is SelectOp.Set)
            {
                Active = -1;
            }

            Flush();
        }

        public void SelectVertices(IEnumerable<int> chosen)
        {
            if (mesh is not { } current)
                return;

            vertices.Clear();

            foreach (var one in chosen)
            {
                if (one >= 0 && one < current.Points.Length && !hiddenVertices.Contains(one))
                    vertices.Add(one);
            }

            Active = -1;
            Lift();
        }

        public void SelectAll()
        {
            if (mesh is not { } current)
                return;

            var target = PartSet();

            target.Clear();

            for (var i = 0; i < CountOf(current, Part); i++)
            {
                if (IsShown(current, i))
                    target.Add(i);
            }

            Flush();
        }

        public void Clear()
        {
            vertices.Clear();
            edges.Clear();
            faces.Clear();
            Active = -1;
            Revision++;
        }

        public void Invert()
        {
            if (mesh is not { } current)
                return;

            var target = PartSet();
            var flipped = Enumerable.Range(0, CountOf(current, Part)).Where(one => IsShown(current, one) && !target.Contains(one)).ToArray();

            target.Clear();
            target.UnionWith(flipped);
            Active = -1;
            Flush();
        }

        public void Hide(bool unselected)
        {
            if (mesh is not { } current)
                return;

            var target = PartSet();
            var chosen = Enumerable.Range(0, CountOf(current, Part)).Where(one => IsShown(current, one) && target.Contains(one) != unselected).ToArray();

            if (chosen.Length == 0)
                return;

            switch (Part)
            {
                case MeshPart.Faces:
                    hiddenFaces.UnionWith(chosen);
                    HideAroundFaces(current);
                    break;

                case MeshPart.Edges:
                    hiddenEdges.UnionWith(chosen);
                    HideAroundEdges(current);
                    break;

                default:
                    hiddenVertices.UnionWith(chosen);
                    HideAroundVertices(current);
                    break;
            }

            vertices.ExceptWith(hiddenVertices);
            edges.ExceptWith(hiddenEdges);
            faces.ExceptWith(hiddenFaces);
            HiddenRevision++;

            if (!IsShown(current, Active))
                Active = -1;

            Flush();
        }

        public void Reveal()
        {
            if (!HasHidden)
                return;

            var target = PartSet();
            var shown = Part switch
            {
                MeshPart.Faces => hiddenFaces,
                MeshPart.Edges => hiddenEdges,
                _ => hiddenVertices,
            };

            target.UnionWith(shown);

            hiddenVertices.Clear();
            hiddenEdges.Clear();
            hiddenFaces.Clear();
            HiddenRevision++;

            Flush();
        }

        public bool IsEdgeShown(int edge) => !hiddenEdges.Contains(edge);

        public bool IsFaceShown(int face) => !hiddenFaces.Contains(face);

        public bool IsVertexShown(int vertex) => !hiddenVertices.Contains(vertex);

        private HashSet<int> PartSet() => Part switch
        {
            MeshPart.Faces => faces,
            MeshPart.Edges => edges,
            _ => vertices,
        };

        private bool IsShown(MeshTopology current, int index)
        {
            if (index < 0 || index >= CountOf(current, Part))
                return false;

            return Part switch
            {
                MeshPart.Faces => !hiddenFaces.Contains(index),
                MeshPart.Edges => !hiddenEdges.Contains(index),
                _ => !hiddenVertices.Contains(index),
            };
        }

        private static int CountOf(MeshTopology current, MeshPart part) => part switch
        {
            MeshPart.Faces => current.Faces.Length,
            MeshPart.Edges => current.Edges.Length,
            _ => current.Points.Length,
        };

        private void Lift()
        {
            if (mesh is not { } current)
                return;

            switch (Part)
            {
                case MeshPart.Edges:
                    edges.Clear();

                    for (var e = 0; e < current.Edges.Length; e++)
                    {
                        if (!hiddenEdges.Contains(e)
                            && vertices.Contains(current.Edges[e].A)
                            && vertices.Contains(current.Edges[e].B))
                        {
                            edges.Add(e);
                        }
                    }

                    break;

                case MeshPart.Faces:
                    faces.Clear();

                    for (var f = 0; f < current.Faces.Length; f++)
                    {
                        if (!hiddenFaces.Contains(f) && current.Faces[f].All(vertices.Contains))
                            faces.Add(f);
                    }

                    break;
            }

            Flush();
        }

        private void Flush()
        {
            Revision++;

            if (mesh is not { } current)
                return;

            switch (Part)
            {
                case MeshPart.Faces:
                    edges.Clear();
                    vertices.Clear();

                    foreach (var f in faces)
                    {
                        edges.UnionWith(current.FaceEdges[f]);
                        vertices.UnionWith(current.Faces[f]);
                    }

                    break;

                case MeshPart.Edges:
                    vertices.Clear();
                    faces.Clear();

                    foreach (var e in edges)
                    {
                        vertices.Add(current.Edges[e].A);
                        vertices.Add(current.Edges[e].B);
                    }

                    for (var f = 0; f < current.Faces.Length; f++)
                    {
                        if (!hiddenFaces.Contains(f) && current.FaceEdges[f].All(edges.Contains))
                            faces.Add(f);
                    }

                    break;

                default:
                    edges.Clear();
                    faces.Clear();

                    for (var e = 0; e < current.Edges.Length; e++)
                    {
                        if (!hiddenEdges.Contains(e)
                            && vertices.Contains(current.Edges[e].A)
                            && vertices.Contains(current.Edges[e].B))
                        {
                            edges.Add(e);
                        }
                    }

                    for (var f = 0; f < current.Faces.Length; f++)
                    {
                        if (!hiddenFaces.Contains(f) && current.Faces[f].All(vertices.Contains))
                            faces.Add(f);
                    }

                    break;
            }
        }

        private void HideAroundVertices(MeshTopology current)
        {
            for (var e = 0; e < current.Edges.Length; e++)
            {
                if (hiddenVertices.Contains(current.Edges[e].A) || hiddenVertices.Contains(current.Edges[e].B))
                    hiddenEdges.Add(e);
            }

            for (var f = 0; f < current.Faces.Length; f++)
            {
                if (current.Faces[f].Any(hiddenVertices.Contains))
                    hiddenFaces.Add(f);
            }
        }

        private void HideAroundEdges(MeshTopology current)
        {
            for (var f = 0; f < current.Faces.Length; f++)
            {
                if (current.FaceEdges[f].Any(hiddenEdges.Contains))
                    hiddenFaces.Add(f);
            }

            for (var v = 0; v < current.Points.Length; v++)
            {
                if (current.VertexEdges[v].Length > 0 && current.VertexEdges[v].All(hiddenEdges.Contains))
                    hiddenVertices.Add(v);
            }
        }

        private void HideAroundFaces(MeshTopology current)
        {
            for (var e = 0; e < current.Edges.Length; e++)
            {
                if (current.EdgeFaces[e].Length > 0 && current.EdgeFaces[e].All(hiddenFaces.Contains))
                    hiddenEdges.Add(e);
            }

            for (var v = 0; v < current.Points.Length; v++)
            {
                if (current.VertexEdges[v].Length > 0 && current.VertexEdges[v].All(hiddenEdges.Contains))
                    hiddenVertices.Add(v);
            }
        }

        private void Trim(MeshTopology current)
        {
            vertices.RemoveWhere(one => one >= current.Points.Length);
            edges.RemoveWhere(one => one >= current.Edges.Length);
            faces.RemoveWhere(one => one >= current.Faces.Length);
            hiddenVertices.RemoveWhere(one => one >= current.Points.Length);
            hiddenEdges.RemoveWhere(one => one >= current.Edges.Length);
            hiddenFaces.RemoveWhere(one => one >= current.Faces.Length);

            if (Active >= CountOf(current, Part))
                Active = -1;

            Revision++;
            HiddenRevision++;
        }

        private void Remap(MeshTopology previous, MeshTopology current)
        {
            var places = PlacesOf(current);
            var chosen = vertices.Select(one => Find(places, previous, one)).Where(one => one >= 0).ToArray();

            RemapHidden(previous, current, places);

            vertices.Clear();
            vertices.UnionWith(chosen.Where(one => !hiddenVertices.Contains(one)));
            Active = -1;
            Lift();
        }

        private void RemapHidden(MeshTopology previous, MeshTopology current) => RemapHidden(previous, current, PlacesOf(current));

        private void RemapHidden(MeshTopology previous, MeshTopology current, Dictionary<(long, long, long), int> places)
        {
            HiddenRevision++;

            if (!HasHidden)
                return;

            var shownVertices = hiddenVertices.Select(one => Find(places, previous, one)).Where(one => one >= 0).ToArray();
            var shownEdges = hiddenEdges
                .Where(one => one < previous.Edges.Length)
                .Select(one => EdgeOf(current, Find(places, previous, previous.Edges[one].A), Find(places, previous, previous.Edges[one].B)))
                .Where(one => one >= 0)
                .ToArray();
            var faceKeys = hiddenFaces
                .Where(one => one < previous.Faces.Length)
                .Select(one => FaceKey(previous.Faces[one].Select(corner => Find(places, previous, corner))))
                .ToHashSet();

            hiddenVertices.Clear();
            hiddenVertices.UnionWith(shownVertices);
            hiddenEdges.Clear();
            hiddenEdges.UnionWith(shownEdges);
            hiddenFaces.Clear();

            for (var f = 0; f < current.Faces.Length; f++)
            {
                if (faceKeys.Contains(FaceKey(current.Faces[f])))
                    hiddenFaces.Add(f);
            }
        }

        private static int EdgeOf(MeshTopology current, int a, int b) => a < 0 || b < 0 ? -1 : current.FindEdge(a, b);

        private static string FaceKey(IEnumerable<int> corners) => string.Join(',', corners.Order());

        private static Dictionary<(long, long, long), int> PlacesOf(MeshTopology current)
        {
            var places = new Dictionary<(long, long, long), int>();

            for (var i = 0; i < current.Points.Length; i++)
                places.TryAdd(KeyOf(current.Points[i]), i);

            return places;
        }

        private static int Find(Dictionary<(long, long, long), int> places, MeshTopology previous, int vertex)
            => vertex >= 0 && vertex < previous.Points.Length && places.TryGetValue(KeyOf(previous.Points[vertex]), out var found) ? found : -1;

        private static (long, long, long) KeyOf(in Vector3 point)
            => ((long)MathF.Round(point.X / Weld), (long)MathF.Round(point.Y / Weld), (long)MathF.Round(point.Z / Weld));
    }
}
