namespace YMM43D.Commons
{
    public static class Live3D
    {
        public static I3DProvider? KeepLast(I3DProvider? found, I3DProvider? last) => found ?? (last is I3DAlive { IsAlive: false } ? null : last);

        public static bool IsAlive(object? provider) => provider is not null and not I3DAlive { IsAlive: false };
    }
}
