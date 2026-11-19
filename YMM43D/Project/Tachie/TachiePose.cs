using YMM43D.Commons;
using YukkuriMovieMaker.Plugin.Tachie;

namespace YMM43D.Project.Tachie
{
    internal readonly record struct TachiePose(float RotationX, float RotationY, float RotationZ)
    {
        public static TachiePose None => new(0f, 0f, 0f);

        public static TachiePose Of(ITachieItemParameter? parameter, in FrameContext time)
        {
            if (parameter is not Model3DItemParameter item)
                return None;

            return new TachiePose(item.RotationX.GetFloat(time), item.RotationY.GetFloat(time), item.RotationZ.GetFloat(time));
        }
    }
}
