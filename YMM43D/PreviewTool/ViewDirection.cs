using YMM43D.Commons;

namespace YMM43D.PreviewTool
{
    internal enum ViewDirection
    {
        Front,
        Back,
        Right,
        Left,
        Top,
        Bottom,
    }

    internal static class ViewDirections
    {
        public static (float Yaw, float Pitch) GetAngles(ViewDirection direction) => direction switch
        {
            ViewDirection.Front => (0f, 0f),
            ViewDirection.Back => (180f, 0f),
            ViewDirection.Right => (90f, 0f),
            ViewDirection.Left => (-90f, 0f),
            ViewDirection.Top => (0f, -CameraState.MaxPitch),
            ViewDirection.Bottom => (0f, CameraState.MaxPitch),
            _ => (0f, 0f),
        };
    }
}
