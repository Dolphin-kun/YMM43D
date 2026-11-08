using System.Numerics;
using YMM43D.Commons;
using YMM43D.Project.Shape;

namespace YMM43D.PreviewTool
{
    internal sealed class OutlineDragController
    {
        private Shape3DParameter? parameter;
        private EditScope scope;
        private int index = -1;

        private Matrix4x4 inverse = Matrix4x4.Identity;
        private Vector3 planePoint;
        private Vector3 planeNormal;
        private Vector2 anchor;
        private Vector2 start;
        private Vector2 applied;

        public bool IsDragging => parameter is not null;

        public bool Begin(in PreviewOutline outline, int point, in PickRay ray, in EditScope edit)
        {
            if (point < 0 || point >= outline.Points.Length || point >= outline.Parameter.Outline.Count)
                return false;

            if (!Matrix4x4.Invert(outline.ToWorld, out inverse))
                return false;

            var facing = Vector3.TransformNormal(Vector3.UnitZ, outline.ToWorld);

            if (facing.LengthSquared() < 1e-12f)
                return false;

            planeNormal = Vector3.Normalize(facing);
            planePoint = outline.Points[point];

            if (ray.IntersectPlane(planePoint, planeNormal) is not { } hit)
                return false;

            var held = outline.Parameter.Outline[point];

            anchor = Flatten(hit);
            start = new Vector2(held.X.GetFloat(outline.Time), held.Y.GetFloat(outline.Time));
            applied = start;

            parameter = outline.Parameter;
            index = point;
            scope = edit;

            return true;
        }

        public bool Update(in PickRay ray, in SnapGrid snap)
        {
            if (parameter is not { } target || index < 0 || index >= target.Outline.Count)
                return false;

            if (ray.IntersectPlane(planePoint, planeNormal) is not { } hit)
                return false;

            var moved = snap.SnapPoint(start + Flatten(hit) - anchor);
            var step = moved - applied;

            if (step == Vector2.Zero)
                return false;

            applied = moved;

            var point = target.Outline[index];

            scope.Nudge(point.X, step.X);
            scope.Nudge(point.Y, step.Y);

            return true;
        }

        public void End()
        {
            parameter = null;
            index = -1;
        }

        private Vector2 Flatten(in Vector3 world)
        {
            var local = Vector3.Transform(world, inverse);

            return new Vector2(local.X, local.Y);
        }
    }
}
