using YMM43D.Camera;

namespace YMM43D.PreviewTool
{
    /// <summary>決まった向きから見るときの、見る方向。</summary>
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
        /// <summary>
        /// その向きから見るときの、水平・垂直の回転角。
        /// </summary>
        /// <remarks>
        /// 真上・真下は視線が定まらなくなる手前で止めます。ちょうど真上から見ると
        /// 水平方向の向きが決められません。
        /// </remarks>
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
