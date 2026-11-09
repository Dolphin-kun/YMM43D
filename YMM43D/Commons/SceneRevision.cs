namespace YMM43D.Commons
{
    public static class SceneRevision
    {
        private static long current;

        [ThreadStatic]
        private static int muted;

        public static long Current => Interlocked.Read(ref current);

        public static void Advance()
        {
            if (muted == 0)
                Interlocked.Increment(ref current);
        }

        public static IDisposable Mute() => new Muting();

        private sealed class Muting : IDisposable
        {
            private bool disposed;

            public Muting() => muted++;

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                muted--;
            }
        }
    }
}
