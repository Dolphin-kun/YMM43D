using System.Numerics;
using YMM43D.Commons;

namespace YMM43D.PreviewTool
{
    internal sealed class HandleMarker(PreviewHandles handles) : ISceneMarkerSource
    {
        public SceneMarker GetMarker(in FrameContext itemTime) => SceneMarker.ForCamera(handles.Position);

        public void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope)
            => handles.Source.MoveHandle(handles.Selected, shift, itemTime, scope);
    }
}
