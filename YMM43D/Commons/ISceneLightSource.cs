using System.Numerics;

namespace YMM43D.Commons
{
    public interface ISceneLightSource
    {
        SceneLight GetLight(in FrameContext itemTime);
    }

    // 自分では位置を持たず、アイテムの置き場所に従って光るもの。
    // 3D図形のように、YMM4 側の座標で動かされるものはこちらを実装する。
    public interface IPlacedSceneLightSource
    {
        bool IsLightEnabled { get; }

        SceneLight GetLight(in FrameContext itemTime, in Matrix4x4 placement);
    }

    public interface ISceneEnvironment
    {
        Vector3 GetAmbient(in FrameContext itemTime);

        SceneFog GetFog(in FrameContext itemTime);
    }
}
