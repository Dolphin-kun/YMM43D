using System.Numerics;

namespace YMM43D.Commons
{
    public interface IPlacedSceneLightSource
    {
        bool IsLightEnabled { get; }

        SceneLight GetLight(in FrameContext itemTime, in Matrix4x4 placement);
    }
}
