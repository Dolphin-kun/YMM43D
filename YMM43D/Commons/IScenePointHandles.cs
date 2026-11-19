using System.Numerics;

namespace YMM43D.Commons
{
    public interface IScenePointHandles
    {
        int SelectedHandle { get; set; }

        int HandleCount { get; }

        Matrix4x4 GetHandleTransform(in FrameContext itemTime);

        Vector3 GetHandle(int index, in FrameContext itemTime);

        void MoveHandle(int index, in Vector3 shift, in FrameContext itemTime, in EditScope scope);
    }
}
