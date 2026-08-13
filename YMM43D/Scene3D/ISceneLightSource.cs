
namespace YMM43D.Scene3D
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
