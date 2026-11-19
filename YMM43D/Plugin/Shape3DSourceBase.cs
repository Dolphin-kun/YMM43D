using System.Numerics;
using Vortice.Direct2D1;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    public abstract class Shape3DSourceBase(IGraphicsDevicesAndContext devices) : IShapeSource2, I3DProvider, I3DBounds, I3DAlive
    {
        private readonly Output3DRenderer renderer = new();
        public bool IsAlive { get; private set; } = true;

        protected IGraphicsDevicesAndContext Devices { get; } = devices;

        internal Action? Disposed { get; set; }

        public ID2D1Image Output
        {
            get => field ?? throw new InvalidOperationException("まだ画像が生成されていません。Update を先に呼んでください。");
            private set;
        }

        public virtual IEnumerable<VideoController> Controllers => [];

        public virtual bool RequiresMappedTexture => false;

        public abstract void Draw(in Render3DContext render, DrawContext3D item);

        protected abstract WorldBounds GetWorldBounds(in FrameContext itemTime);

        WorldBounds I3DBounds.GetLocalBounds(in FrameContext itemTime) => GetWorldBounds(itemTime);

        public void Update(TimelineItemSourceDescription timelineItemSourceDescription)
        {
            Prepare(timelineItemSourceDescription);

            var itemTime = FrameContext.FromItem(timelineItemSourceDescription);

            Output = renderer.Render(Devices, timelineItemSourceDescription, GetWorldBounds(itemTime), Matrix4x4.Identity, Draw, out _, self: this, hostAppliesPlacement: true);

            SceneRevision.Advance();
        }

        protected virtual void Prepare(TimelineItemSourceDescription description)
        {
        }

        public virtual void Dispose()
        {
            IsAlive = false;
            Disposed?.Invoke();
            Disposed = null;
            renderer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
