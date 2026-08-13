using YMM43D.Scene3D;

namespace YMM43D.Lighting
{
    public interface ISceneLightSource
    {
        SceneLight GetLight(in FrameContext itemTime);
    }

    public interface ISceneEnvironment
    {
        System.Numerics.Vector3 GetAmbient(in FrameContext itemTime);

        SceneFog GetFog(in FrameContext itemTime);
    }
}
