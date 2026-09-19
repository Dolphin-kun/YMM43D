using System.Runtime.CompilerServices;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public static class Provider3DRegistry
    {
        private static readonly ConditionalWeakTable<object, Registrations> registry = [];

        [ThreadStatic]
        private static int suppressionDepth;

        public static bool IsSuppressed => suppressionDepth > 0;

        public static IDisposable SuppressRegistration() => new Suppression();

        public static void Register(object parameter, I3DProvider provider)
            => Register(parameter, provider, null);

        public static void Register(object parameter, I3DProvider provider, IGraphicsDevicesAndContext? devices)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(provider);

            if (suppressionDepth > 0)
                return;

            registry.GetOrCreateValue(parameter).Add(provider, devices);
        }

        public static void Unregister(object parameter, I3DProvider provider)
        {
            if (registry.TryGetValue(parameter, out var registrations) && registrations.Remove(provider))
                registry.Remove(parameter);
        }

        public static I3DProvider? Find(object? parameter) => Find(parameter, null);

        public static I3DProvider? Find(object? parameter, IGraphicsDevicesAndContext? devices)
        {
            if (parameter is null || !registry.TryGetValue(parameter, out var registrations))
                return null;

            return registrations.Find(devices);
        }

        private sealed class Registrations
        {
            private readonly List<(I3DProvider Provider, object? Source)> entries = [];

            public void Add(I3DProvider provider, IGraphicsDevicesAndContext? devices)
            {
                lock (entries)
                {
                    var key = SourceKey.Of(devices);

                    entries.RemoveAll(entry => ReferenceEquals(entry.Provider, provider)
                        || (key is not null && ReferenceEquals(entry.Source, key)));
                    entries.Add((provider, key));
                }
            }

            public bool Remove(I3DProvider provider)
            {
                lock (entries)
                {
                    entries.RemoveAll(entry => ReferenceEquals(entry.Provider, provider));
                    return entries.Count == 0;
                }
            }

            public I3DProvider? Find(IGraphicsDevicesAndContext? devices)
            {
                lock (entries)
                {
                    if (entries.Count == 0)
                        return null;

                    if (SourceKey.Of(devices) is { } key)
                    {
                        foreach (var entry in entries)
                        {
                            if (ReferenceEquals(entry.Source, key))
                                return entry.Provider;
                        }
                    }

                    return entries[^1].Provider;
                }
            }
        }

        private sealed class Suppression : IDisposable
        {
            private bool disposed;

            public Suppression() => suppressionDepth++;

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                suppressionDepth--;
            }
        }
    }
}
