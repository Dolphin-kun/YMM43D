using System.Runtime.CompilerServices;

namespace YMM43D.Commons
{
    public static class Provider3DRegistry
    {
        private static readonly ConditionalWeakTable<object, I3DProvider> registry = [];

        [ThreadStatic]
        private static int suppressionDepth;

        public static IDisposable SuppressRegistration() => new Suppression();

        public static void Register(object parameter, I3DProvider provider)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(provider);

            if (suppressionDepth > 0)
                return;

            registry.Remove(parameter);
            registry.Add(parameter, provider);
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

        public static I3DProvider? Find(object? parameter)
        {
            if (parameter is null)
                return null;

            registry.TryGetValue(parameter, out var provider);
            return provider;
        }
    }
}
