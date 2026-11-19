using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public static class DisposeCollectorExtensions
    {
        public static T CollectEx<T>(this DisposeCollector disposer, T resource) where T : IDisposable?
        {
            if (resource is not null)
                disposer.Collect(resource);

            return resource;
        }
    }
}
