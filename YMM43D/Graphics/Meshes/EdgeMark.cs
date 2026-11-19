namespace YMM43D.Graphics.Meshes
{
    public readonly record struct EdgeMark(bool Sharp, float Crease, bool Seam = false)
    {
        public bool IsEmpty => !Sharp && !Seam && Crease <= 0f;
    }
}
