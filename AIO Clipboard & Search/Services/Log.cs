using System;
using System.Diagnostics;
using System.IO;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Minimal file logger. Writes to AIO_Cache/app.log next to the executable so
    /// users can attach logs to bug reports; mirrors everything to the debugger.
    /// Logging must never throw or block the app.
    /// </summary>
    internal static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new();

        private static string LogFile => Path.Combine(AppPaths.CacheFolder, "app.log");

        public static void Info(string message)  => Write("INFO", message);
        public static void Warn(string message)  => Write("WARN", message);
        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex == null ? message : $"{message}: {ex}");

        private static void Write(string level, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            Debug.WriteLine($"[AIO] {line}");
            try
            {
                lock (Gate)
                {
                    AppPaths.EnsureCacheFolder();
                    var info = new FileInfo(LogFile);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        // Keep exactly one rotated file so logs can't grow unbounded.
                        File.Copy(LogFile, LogFile + ".old", overwrite: true);
                        File.Delete(LogFile);
                    }
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Swallow: a broken log target must never take the app down.
            }
        }
    }
}
