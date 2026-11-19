using System.Numerics;
using YMM43D.Commons;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal readonly record struct PreviewBone(
        IVideoItem Item,
        IBoneHost Parameter,
        FrameContext Time,
        int Index,
        string Name,
        string ParentName,
        Vector3 Position,
        Vector3 Tail,
        Vector3 ParentTail,
        bool HasParent,
        bool HasChildren,
        Matrix4x4 Frame)
    {
        public Matrix4x4 HandleOrientation(float x, float y)
        {
            var second = Matrix4x4.CreateRotationX(float.DegreesToRadians(x)) * Frame;
            var third = Matrix4x4.CreateRotationY(float.DegreesToRadians(y))
                      * Matrix4x4.CreateRotationX(float.DegreesToRadians(x)) * Frame;

            var made = Matrix4x4.Identity;

            SetRow(ref made, 0, Normalized(Frame, 0));
            SetRow(ref made, 1, Normalized(second, 1));
            SetRow(ref made, 2, Normalized(third, 2));

            return made;
        }

        private static Vector3 Normalized(in Matrix4x4 matrix, int row)
        {
            var value = row switch
            {
                0 => new Vector3(matrix.M11, matrix.M12, matrix.M13),
                1 => new Vector3(matrix.M21, matrix.M22, matrix.M23),
                _ => new Vector3(matrix.M31, matrix.M32, matrix.M33),
            };

            return value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : value;
        }

        private static void SetRow(ref Matrix4x4 matrix, int row, in Vector3 value)
        {
            switch (row)
            {
                case 0: matrix.M11 = value.X; matrix.M12 = value.Y; matrix.M13 = value.Z; break;
                case 1: matrix.M21 = value.X; matrix.M22 = value.Y; matrix.M23 = value.Z; break;
                default: matrix.M31 = value.X; matrix.M32 = value.Y; matrix.M33 = value.Z; break;
            }
        }
    }
}
