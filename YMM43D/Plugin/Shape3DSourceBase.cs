using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    public abstract class Shape3DSourceBase : IShapeSource2, I3DProvider, I3DBounds
    {
        private readonly Output3DRenderer renderer = new();
        private ID2D1Image? output;

        protected IGraphicsDevicesAndContext Devices { get; }

        protected Shape3DSourceBase(IGraphicsDevicesAndContext devices)
        {
            Devices = devices;
        }

        public ID2D1Image Output => output ?? throw new InvalidOperationException(
            "まだ画像が生成されていません。Update を先に呼んでください。");

        public virtual IEnumerable<VideoController> Controllers => [];

        public virtual bool RequiresMappedTexture => false;

        public abstract void Draw(in Render3DContext render, DrawContext3D item);

        protected abstract WorldBounds GetWorldBounds(in FrameContext itemTime);

        WorldBounds I3DBounds.GetLocalBounds(in FrameContext itemTime) => GetWorldBounds(itemTime);

        public void Update(TimelineItemSourceDescription description)
        {
            var itemTime = FrameContext.FromItem(description);

            output = renderer.Render(
                Devices,
                description,
                GetWorldBounds(itemTime),
                Matrix4x4.Identity,
                Draw,
                self: this,
                hostAppliesPlacement: true);
        }

        public virtual void Dispose()
        {
            renderer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
