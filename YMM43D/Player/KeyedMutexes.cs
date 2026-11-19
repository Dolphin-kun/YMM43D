using Vortice.DXGI;

namespace YMM43D.Player
{
    internal static class KeyedMutexes
    {
        public const ulong WriteKey = 0;
        public const ulong ReadKey = 1;
        private const int SyncTimeoutMs = 500;

        public static bool TryAcquire(IDXGIKeyedMutex mutex, ulong key)
        {
            try
            {
                mutex.AcquireSync(key, SyncTimeoutMs);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
