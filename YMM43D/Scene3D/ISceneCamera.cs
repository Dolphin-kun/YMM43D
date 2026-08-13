
namespace YMM43D.Scene3D
{
    public interface ISceneCamera
    {
        CameraState GetState(in FrameContext itemTime);

        void Move(in CameraMove move, in EditScope scope);
    }
}
