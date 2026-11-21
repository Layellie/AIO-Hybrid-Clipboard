using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Detects sensitive clipboard payloads placed by password managers and privacy-aware apps
    /// (Windows Hello, KeePass, Bitwarden, 1Password, browsers in private mode, etc.) as well as
    /// user-configured excluded applications, so secrets are never recorded into history.
    /// </summary>
    internal static class ClipboardPrivacyGuard
    {
        internal const string ExcludeMonitorFormat        = "ExcludeClipboardContentFromMonitorProcessing";
        internal const string ClipboardViewerIgnoreFormat = "Clipboard Viewer Ignore";
        internal const string CanIncludeInHistoryFormat   = "CanIncludeInClipboardHistory";
        internal const string ConcealedTypeFormat         = "org.nspasteboard.ConcealedType";
        internal const string TransientTypeFormat         = "org.nspasteboard.TransientType";

        private static readonly string[] PresenceBlockFormats =
        {
            ExcludeMonitorFormat,
            ClipboardViewerIgnoreFormat,
            ConcealedTypeFormat,
            TransientTypeFormat,
        };

        // Cached Win32 clipboard format IDs (immutable for the lifetime of the Windows session).
        private static readonly Lazy<uint[]> PresenceBlockIds = new(() =>
        {
            var ids = new uint[PresenceBlockFormats.Length];
            for (int i = 0; i < PresenceBlockFormats.Length; i++)
                ids[i] = RegisterClipboardFormat(PresenceBlockFormats[i]);
            return ids;
        });

        private static readonly Lazy<uint> CanIncludeInHistoryId =
            new(() => RegisterClipboardFormat(CanIncludeInHistoryFormat));

        /// <summary>
        /// Parses a comma/semicolon/newline-separated list of excluded process names
        /// (e.g. "KeePassXC.exe, mstsc, 1Password.exe") into a normalized lookup set.
        /// </summary>
        internal static HashSet<string> ParseExcludedApps(string? rawList)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rawList))
                return set;

            string[] parts = rawList.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string clean = part.Trim().Trim('"');
                if (clean.Length == 0) continue;

                // Normalize so both "mstsc" and "mstsc.exe" match "mstsc.exe".
                string fileName = Path.GetFileName(clean);
                if (fileName.Length == 0) continue;

                set.Add(fileName);
                if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    set.Add(fileName + ".exe");
            }
            return set;
        }

        internal static bool IsProcessExcluded(string? processName, IReadOnlySet<string> excludedApps)
        {
            if (string.IsNullOrWhiteSpace(processName) || excludedApps.Count == 0)
                return false;

            return excludedApps.Contains(processName) ||
                   excludedApps.Contains(Path.GetFileNameWithoutExtension(processName));
        }

        /// <summary>
        /// Resolves the executable filename (e.g. "Code.exe") for the current clipboard owner
        /// (falling back to the active foreground window) via lightweight Win32 handles.
        /// </summary>
        public static string GetClipboardSourceAppName()
        {
            IntPtr hwnd = GetClipboardOwner();
            if (hwnd == IntPtr.Zero)
                hwnd = GetForegroundWindow();

            return TryGetWindowProcessName(hwnd);
        }

        public static string TryGetWindowProcessName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return string.Empty;

            try
            {
                _ = GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return string.Empty;

                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero)
                    return string.Empty;

                try
                {
                    char[] buf = new char[512];
                    int size = buf.Length;
                    if (!QueryFullProcessImageName(hProcess, 0, buf, ref size) || size <= 0)
                        return string.Empty;

                    string fullPath = new string(buf, 0, size);
                    return Path.GetFileName(fullPath);
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Pure decision rule for unit testing without touching the live system clipboard.
        /// </summary>
        internal static bool ShouldBlock(Func<string, bool> hasFormat, Func<string, int?> readDword)
        {
            foreach (string format in PresenceBlockFormats)
            {
                if (hasFormat(format))
                    return true;
            }

            if (hasFormat(CanIncludeInHistoryFormat))
            {
                int? value = readDword(CanIncludeInHistoryFormat);
                // Per Microsoft Clipboard History spec: DWORD 0 explicitly forbids recording.
                if (value == 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Inspects the current system clipboard for password-manager / privacy exclusion markers.
        /// </summary>
        public static bool ShouldIgnoreCurrentClipboard(IntPtr ownerHwnd)
        {
            try
            {
                foreach (uint fmt in PresenceBlockIds.Value)
                {
                    if (fmt != 0 && IsClipboardFormatAvailable(fmt))
                        return true;
                }

                uint historyFmt = CanIncludeInHistoryId.Value;
                if (historyFmt != 0 && IsClipboardFormatAvailable(historyFmt))
                {
                    int? flag = TryReadClipboardDword(ownerHwnd, historyFmt);
                    if (flag == 0)
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Privacy guard check failed: {ex.Message}");
            }

            return false;
        }

        private static int? TryReadClipboardDword(IntPtr ownerHwnd, uint format)
        {
            if (!OpenClipboard(ownerHwnd))
                return null;

            try
            {
                IntPtr hMem = GetClipboardData(format);
                if (hMem == IntPtr.Zero)
                    return null;

                ulong size = GlobalSize(hMem).ToUInt64();
                if (size < sizeof(int))
                    return null;

                IntPtr ptr = GlobalLock(hMem);
                if (ptr == IntPtr.Zero)
                    return null;

                try
                {
                    return Marshal.ReadInt32(ptr);
                }
                finally
                {
                    GlobalUnlock(hMem);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
    }
}
