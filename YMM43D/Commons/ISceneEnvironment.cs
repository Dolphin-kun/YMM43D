using System.Numerics;
using YMM43D.Graphics;

namespace YMM43D.Commons
{
    public interface ISceneEnvironment
    {
        Vector3 GetAmbient(in FrameContext itemTime);

        SceneFog GetFog(in FrameContext itemTime);

        Vector3? GetGround(in FrameContext itemTime) => null;

        SceneMirror GetMirror(in FrameContext itemTime) => SceneMirror.None;

        int ShadowResolution => ShadowMapArray.DefaultSize;
    }
}
