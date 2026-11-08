using System.Numerics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public interface ISceneRotation3D
    {
        Animation RotationX { get; }

        Animation RotationY { get; }

        Animation RotationZ { get; }

        Matrix4x4 ToOrientation(float x, float y, float z) => Rotation3D.ForObject(x, y, z);

        Vector3 ToAngles(in Matrix4x4 orientation) => Rotation3D.ToAngles(orientation);
    }
}
