namespace YMM43D.Graphics.Meshes
{
    public readonly record struct MeshEdge(int A, int B)
    {
        public int Other(int vertex) => vertex == A ? B : A;

        public bool Holds(int vertex) => vertex == A || vertex == B;
    }
}
