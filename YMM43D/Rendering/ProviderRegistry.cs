using System.Runtime.CompilerServices;

namespace YMM43D.Rendering
{
    public static class ProviderRegistry
    {
        private static readonly ConditionalWeakTable<object, I3DProvider> registry = [];

        public static void Register(object parameter, I3DProvider provider)
        {
            if (parameter == null || provider == null) return;
            registry.Remove(parameter);
            registry.Add(parameter, provider);
        }

        public static I3DProvider? GetProvider(object? parameter)
        {
            if (parameter == null) return null;
            registry.TryGetValue(parameter, out var provider);
            return provider;
        }
    }
}
