using System.Numerics;

namespace YMM43D.Commons
{
    public interface ISceneCamera
    {
        CameraState GetState(in FrameContext itemTime);

        void Move(in CameraMove move, in FrameContext itemTime, in EditScope scope);

        Vector3? GetPivot(in FrameContext itemTime) => null;
    }
}
