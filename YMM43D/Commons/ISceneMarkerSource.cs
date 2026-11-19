using System.Numerics;

namespace YMM43D.Commons
{
    public interface ISceneMarkerSource
    {
        SceneMarker GetMarker(in FrameContext itemTime);

        void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope);
    }
}
