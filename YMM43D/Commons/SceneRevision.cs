namespace YMM43D.Commons
{
    // YMM4 が 3D の部品を描き直すたびに進む番号。3Dプレビューはこれを見て、必要なときだけ描き直す。
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

        // 3Dプレビュー自身が部品を動かしたときに、それを変化と数えないようにする。
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
