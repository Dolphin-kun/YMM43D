using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;

namespace YMM43D.PreviewTool
{
    internal sealed class MeshSlideController
    {
        private Shape3DParameter? parameter;
        private MeshTopology? mesh;
        private int[] chosen = [];
        private Vector3[] startPoints = [];
        private (int Forward, int Backward)[] rails = [];

        private Vector3 planePoint;
        private Vector3 planeNormal;
        private Vector3 anchor;
        private Vector3 rail;
        private float applied;

        public bool IsDragging => parameter is not null;

        public static bool CanSlide(in MeshEditTarget target, IReadOnlyCollection<int> selection)
        {
            if (selection.Count == 0)
                return false;

            var rails = target.Mesh.SlideRails(new HashSet<int>(selection));

            foreach (var one in selection)
            {
                if (one >= 0 && one < rails.Length && rails[one].Forward >= 0)
                    return true;
            }

            return false;
        }

        public bool Begin(
            in MeshEditTarget target,
            IReadOnlyCollection<int> selection,
            in PickRay ray,
            in Vector3 viewDirection)
        {
            if (selection.Count == 0 || target.Item.IsLocked)
                return false;

            var built = target.Parameter.EnsureMesh();
            var picked = new HashSet<int>(selection.Where(one => one >= 0 && one < built.Points.Length));

            if (picked.Count == 0)
                return false;

            rails = built.SlideRails(picked);
            chosen = [.. picked.Where(one => rails[one].Forward >= 0)];

            if (chosen.Length == 0)
                return false;

            startPoints = [.. built.Points];

            var first = chosen[0];

            planePoint = Vector3.Transform(startPoints[first], target.ToWorld);
            rail = Vector3.Transform(startPoints[rails[first].Forward], target.ToWorld) - planePoint;

            if (rail.LengthSquared() < 1e-12f)
                return false;

            planeNormal = -viewDirection;
            anchor = ray.IntersectPlane(planePoint, planeNormal) ?? planePoint;
            applied = 0f;

            parameter = target.Parameter;
            mesh = built;

            return true;
        }

        public bool Update(in PickRay ray)
        {
            if (parameter is not { } target || mesh is not { } built)
                return false;

            if (ray.IntersectPlane(planePoint, planeNormal) is not { } hit)
                return false;

            var wanted = Math.Clamp(
                Vector3.Dot(hit - anchor, rail) / rail.LengthSquared(), -1f, 1f);

            if (MathF.Abs(wanted - applied) < 1e-4f)
                return false;

            applied = wanted;

            var points = (Vector3[])startPoints.Clone();

            foreach (var index in chosen)
            {
                var (forward, backward) = rails[index];
                var toward = wanted >= 0f ? forward : backward;

                if (toward < 0 || toward >= points.Length)
                    continue;

                points[index] = Vector3.Lerp(startPoints[index], startPoints[toward], MathF.Abs(wanted));
            }

            mesh = target.StorePoints(built.WithPoints(points));

            return true;
        }

        public void End()
        {
            parameter = null;
            mesh = null;
            chosen = [];
            startPoints = [];
            rails = [];
        }
    }
}
