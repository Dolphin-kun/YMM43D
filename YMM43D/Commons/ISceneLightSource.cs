using System.Numerics;

namespace YMM43D.Commons
{
    public interface ISceneLightSource
    {
        SceneLight GetLight(in FrameContext itemTime);
    }

    public interface IPlacedSceneLightSource
    {
        bool IsLightEnabled { get; }

        SceneLight GetLight(in FrameContext itemTime, in Matrix4x4 placement);
    }

    public interface ISceneEnvironment
    {
        Vector3 GetAmbient(in FrameContext itemTime);

        SceneFog GetFog(in FrameContext itemTime);

        int ShadowResolution => Graphics.ShadowMapArray.DefaultSize;
    }
}
