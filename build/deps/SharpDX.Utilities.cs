namespace SharpDX
{
    /// <summary>Shim minimal remplaçant SharpDX.Utilities (seul SizeOf est utilisé).</summary>
    public static class Utilities
    {
        public static unsafe int SizeOf<T>() where T : unmanaged => sizeof(T);
    }
}
