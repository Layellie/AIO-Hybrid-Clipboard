using System;
using System.IO;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>Well-known filesystem locations shared across services.</summary>
    internal static class AppPaths
    {
        private static string? _cacheFolderOverride;

        public static string CacheFolder =>
            _cacheFolderOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache");

        public static string SessionFile => Path.Combine(CacheFolder, "session.json");

        public static void EnsureCacheFolder() => Directory.CreateDirectory(CacheFolder);

        /// <summary>
        /// Temporarily redirects <see cref="CacheFolder"/> and <see cref="SessionFile"/>
        /// for isolated unit tests. Dispose the returned scope to restore the original path.
        /// </summary>
        internal static IDisposable OverrideCacheFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Cache folder override cannot be empty.", nameof(folder));

            string? previous = _cacheFolderOverride;
            _cacheFolderOverride = folder;
            return new RestoreScope(previous);
        }

        private sealed class RestoreScope(string? previous) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _cacheFolderOverride = previous;
            }
        }
    }
}
