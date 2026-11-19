using System.Numerics;

namespace Reflection3D
{
    internal static class MirrorCamera
    {
        private const float ClipBias = 1e-3f;
        private const float LeastHeight = 1e-4f;
        private const float FaceNear = 0.01f;
        private const float FaceFar = 1000f;

        private static readonly (Vector3 Forward, Vector3 Up)[] Faces =
        [
            (Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitX, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ),
            (-Vector3.UnitY, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitY),
            (-Vector3.UnitZ, Vector3.UnitY),
        ];

        public static int FaceCount => Faces.Length;

        public static bool TryReflect(
            in Plane mirror,
            in Matrix4x4 view,
            in Matrix4x4 projection,
            in Vector3 camera,
            out Matrix4x4 mirroredView,
            out Matrix4x4 clippedProjection)
        {
            mirroredView = view;
            clippedProjection = projection;

            if (Plane.DotCoordinate(mirror, camera) <= LeastHeight || !Matrix4x4.Invert(view, out var inverse))
                return false;

            mirroredView = Matrix4x4.CreateReflection(mirror) * view;

            var behind = new Vector4(-mirror.Normal, -mirror.D + ClipBias);
            var viewPlane = Vector4.Transform(behind, Matrix4x4.Transpose(inverse));

            clippedProjection = Oblique(projection, viewPlane);

            return true;
        }

        public static Matrix4x4 FaceView(in Vector3 center, int face) => Matrix4x4.CreateLookAt(center, center + Faces[face].Forward, Faces[face].Up);

        public static Matrix4x4 FaceProjection => Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, FaceNear, FaceFar);

        private static Matrix4x4 Oblique(in Matrix4x4 projection, in Vector4 viewPlane)
        {
            if (!Matrix4x4.Invert(projection, out var inverse))
                return projection;

            var corner = Vector4.Transform(new Vector4(MathF.Sign(viewPlane.X), MathF.Sign(viewPlane.Y), 1f, 1f), inverse);
            var scale = Vector4.Dot(viewPlane, corner);

            if (MathF.Abs(scale) < 1e-12f)
                return projection;

            var column = viewPlane / scale;
            var result = projection;

            result.M13 = column.X;
            result.M23 = column.Y;
            result.M33 = column.Z;
            result.M43 = column.W;

            return result;
        }
    }
}
