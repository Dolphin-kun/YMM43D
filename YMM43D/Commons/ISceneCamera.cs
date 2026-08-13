
namespace YMM43D.Commons
{
    public interface ISceneCamera
    {
        CameraState GetState(in FrameContext itemTime);

        void Move(in CameraMove move, in EditScope scope);
    }
}
