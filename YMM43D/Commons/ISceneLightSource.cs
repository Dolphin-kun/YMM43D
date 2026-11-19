namespace YMM43D.Commons
{
    public interface ISceneLightSource
    {
        SceneLight GetLight(in FrameContext itemTime);
    }
}
