using YMM43D.Scene3D;

namespace YMM43D.Camera
{
    public interface ISceneCamera
    {
        CameraState GetState(in FrameContext itemTime);

        void Move(in CameraMove move, in EditScope scope);
    }
}
