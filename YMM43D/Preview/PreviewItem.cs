using YMM43D.Rendering;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Preview
{
    public class PreviewItem(I3DProvider provider, IVideoItem item, int frame, int length)
    {
        public I3DProvider Provider { get; } = provider;
        public IVideoItem Item { get; } = item;
        public int ItemFrame { get; } = frame;
        public int ItemLength { get; } = length;
        public object? VideoSource { get; set; }
    }
}
