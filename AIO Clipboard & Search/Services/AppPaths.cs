using System;
using System.IO;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>Well-known filesystem locations shared across services.</summary>
    internal static class AppPaths
    {
        public static string CacheFolder { get; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache");

        public static string SessionFile { get; } = Path.Combine(CacheFolder, "session.json");

        public static void EnsureCacheFolder() => Directory.CreateDirectory(CacheFolder);
    }
}
