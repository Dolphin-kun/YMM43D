
namespace YMM43D.Commons
{
    public interface ISceneCamera
    {
        CameraState GetState(in FrameContext itemTime);

        void Move(in CameraMove move, in FrameContext itemTime, in EditScope scope);
    }
}
