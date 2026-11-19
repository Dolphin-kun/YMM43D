using System.Numerics;

namespace YMM43D.PreviewTool.Rendering
{
    internal readonly record struct MeshOverlay(Vector3 Right, Vector3 Up, int CutEdge, bool ShowsHandle, bool XRay, long MeshRevision)
    {
        public bool Matches(in MeshOverlay other)
            => CutEdge == other.CutEdge
            && ShowsHandle == other.ShowsHandle
            && XRay == other.XRay
            && MeshRevision == other.MeshRevision
            && Vector3.Distance(Right, other.Right) < 0.01f
            && Vector3.Distance(Up, other.Up) < 0.01f;
    }
}
