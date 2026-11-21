using AIO_Hybrid_Clipboard.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;

namespace AIO_Hybrid_Clipboard.Services
{
    internal static class SessionStore
    {
        private static readonly object IoGate    = new();
        private static readonly object TimerGate = new();

        private static Timer? _debounceTimer;
        private static (List<ClipEntryDto> Texts, List<ScreenshotEntryDto> Shots, bool Encrypt)? _pendingSnapshot;

        private static string FilePath => AppPaths.SessionFile;

        private sealed record ClipEntryDto(string Text, bool IsPinned, DateTime CreatedAt, string SourceApp, string Alias);

        private sealed record ScreenshotEntryDto(
            string Name,
            string Path,
            string OcrText,
            bool IsPinned,
            string ContentHash,
            string SourceApp,
            int PixelWidth,
            int PixelHeight,
            DateTime CapturedAt);

        private sealed record SessionDocumentDto(
            int Version,
            List<ClipEntryDto> ClipboardEntries,
            List<ScreenshotEntryDto> Screenshots);

        /// <summary>
        /// Schedules an atomic background write after <paramref name="delayMs"/> of inactivity.
        /// Must be called from the thread owning the collections (snapshots them immediately).
        /// </summary>
        public static void ScheduleSave(
            IEnumerable<ClipItem> texts,
            IEnumerable<ScreenshotModel> screenshots,
            bool encrypt = false,
            int delayMs = 1500)
        {
            var snapshot = Snapshot(texts, screenshots, encrypt);
            lock (TimerGate)
            {
                _pendingSnapshot = snapshot;
                _debounceTimer ??= new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
                _debounceTimer.Change(delayMs, Timeout.Infinite);
            }
        }

        /// <summary>Cancels any pending debounced write and persists immediately (atomic write).</summary>
        public static void Save(
            IEnumerable<ClipItem> texts,
            IEnumerable<ScreenshotModel> screenshots,
            bool encrypt = false)
        {
            CancelPendingDebounce();
            WriteAtomic(Snapshot(texts, screenshots, encrypt));
        }

        /// <summary>
        /// Final synchronous flush on application exit: disposes the debounce timer and
        /// performs a single deterministic atomic write.
        /// </summary>
        public static void ShutdownAndSave(
            IEnumerable<ClipItem> texts,
            IEnumerable<ScreenshotModel> screenshots,
            bool encrypt = false)
        {
            lock (TimerGate)
            {
                _debounceTimer?.Dispose();
                _debounceTimer   = null;
                _pendingSnapshot = null;
            }
            WriteAtomic(Snapshot(texts, screenshots, encrypt));
        }

        private static void CancelPendingDebounce()
        {
            lock (TimerGate)
            {
                _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _pendingSnapshot = null;
            }
        }

        private static void OnDebounceElapsed(object? state)
        {
            (List<ClipEntryDto> Texts, List<ScreenshotEntryDto> Shots, bool Encrypt)? snapshot;
            lock (TimerGate)
            {
                snapshot = _pendingSnapshot;
                _pendingSnapshot = null;
            }

            if (snapshot != null)
                WriteAtomic(snapshot.Value);
        }

        private static (List<ClipEntryDto> Texts, List<ScreenshotEntryDto> Shots, bool Encrypt) Snapshot(
            IEnumerable<ClipItem> texts, IEnumerable<ScreenshotModel> screenshots, bool encrypt)
        {
            var textDtos = texts.Select(c => new ClipEntryDto(
                c.Text, c.IsPinned, c.CreatedAt, c.SourceApp, c.Alias)).ToList();

            var shotDtos = screenshots.Select(s => new ScreenshotEntryDto(
                s.Name,
                s.Path,
                s.OcrText,
                s.IsPinned,
                s.ContentHash,
                s.SourceApp,
                s.PixelWidth,
                s.PixelHeight,
                s.CapturedAt)).ToList();

            return (textDtos, shotDtos, encrypt);
        }

        private static void WriteAtomic((List<ClipEntryDto> Texts, List<ScreenshotEntryDto> Shots, bool Encrypt) snapshot)
        {
            lock (IoGate)
            {
                string target = FilePath;
                string tmp    = target + ".tmp";
                try
                {
                    AppPaths.EnsureCacheFolder();

                    var doc = new SessionDocumentDto(2, snapshot.Texts, snapshot.Shots);
                    byte[] payload = JsonSerializer.SerializeToUtf8Bytes(doc);

                    if (snapshot.Encrypt)
                        payload = SessionCrypto.Protect(payload);

                    File.WriteAllBytes(tmp, payload);
                    File.Move(tmp, target, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log.Error("Session save failed", ex);
                    try { if (File.Exists(tmp)) File.Delete(tmp); }
                    catch { /* best-effort cleanup of temp file */ }
                }
            }
        }

        /// <summary>Deletes cached PNG files no longer referenced by a live screenshot.</summary>
        public static void CleanOrphanCache(IEnumerable<string> keepPaths)
        {
            try
            {
                string folder = AppPaths.CacheFolder;
                if (!Directory.Exists(folder)) return;

                var keep = new HashSet<string>(
                    keepPaths.Where(p => !string.IsNullOrEmpty(p)).Select(Path.GetFullPath),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.GetFiles(folder, "*.png"))
                {
                    if (keep.Contains(Path.GetFullPath(file))) continue;
                    try { File.Delete(file); }
                    catch (Exception ex) { Log.Warn($"Orphan cache delete failed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Log.Warn($"Cache cleanup failed: {ex.Message}"); }
        }

        public static (List<ClipItem> texts, List<ScreenshotModel> screenshots) Load()
        {
            var texts = new List<ClipItem>();
            var shots = new List<ScreenshotModel>();

            lock (IoGate)
            {
                try
                {
                    if (!File.Exists(FilePath)) return (texts, shots);

                    byte[] rawBytes = File.ReadAllBytes(FilePath);
                    if (rawBytes.Length == 0) return (texts, shots);

                    if (SessionCrypto.IsEncrypted(rawBytes))
                    {
                        if (!SessionCrypto.TryUnprotect(rawBytes, out rawBytes))
                        {
                            Log.Error("Encrypted session file could not be decrypted with current Windows user credentials.");
                            return (texts, shots);
                        }
                    }

                    using var doc = JsonDocument.Parse(rawBytes);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("ClipboardEntries", out var entries) &&
                        entries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                            // Back-compat: v1.0 sessions stored plain strings.
                            if (entry.ValueKind == JsonValueKind.String)
                            {
                                string? legacy = entry.GetString();
                                if (!string.IsNullOrEmpty(legacy))
                                    texts.Add(new ClipItem(legacy));
                                continue;
                            }

                            if (entry.ValueKind != JsonValueKind.Object) continue;

                            string? text = entry.TryGetProperty("Text", out var tv) ? tv.GetString() : null;
                            if (string.IsNullOrEmpty(text)) continue;

                            bool    pinned    = entry.TryGetProperty("IsPinned",  out var pv) && pv.ValueKind == JsonValueKind.True;
                            string? sourceApp = entry.TryGetProperty("SourceApp", out var sv) ? sv.GetString() : null;
                            string? alias     = entry.TryGetProperty("Alias",     out var av) ? av.GetString() : null;
                            DateTime? createdAt = entry.TryGetProperty("CreatedAt", out var cv) &&
                                                  cv.ValueKind == JsonValueKind.String &&
                                                  cv.TryGetDateTime(out var dt)
                                ? dt
                                : null;

                            texts.Add(new ClipItem(text, createdAt, sourceApp, alias) { IsPinned = pinned });
                        }
                    }

                    if (root.TryGetProperty("Screenshots", out var screenshots) &&
                        screenshots.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in screenshots.EnumerateArray())
                        {
                            if (s.ValueKind != JsonValueKind.Object) continue;

                            string name        = s.TryGetProperty("Name",        out var n)  ? n.GetString() ?? "" : "";
                            string path        = s.TryGetProperty("Path",        out var p)  ? p.GetString() ?? "" : "";
                            string ocrText     = s.TryGetProperty("OcrText",     out var o)  ? o.GetString() ?? "" : "";
                            string contentHash = s.TryGetProperty("ContentHash", out var ch) ? ch.GetString() ?? "" : "";
                            string sourceApp   = s.TryGetProperty("SourceApp",   out var sa) ? sa.GetString() ?? "" : "";
                            bool   pinned      = s.TryGetProperty("IsPinned",    out var sp) && sp.ValueKind == JsonValueKind.True;
                            int    pixelWidth  = s.TryGetProperty("PixelWidth",  out var pw) && pw.TryGetInt32(out int w) ? w : 0;
                            int    pixelHeight = s.TryGetProperty("PixelHeight", out var ph) && ph.TryGetInt32(out int h) ? h : 0;
                            DateTime capturedAt = s.TryGetProperty("CapturedAt", out var ca) &&
                                                  ca.ValueKind == JsonValueKind.String &&
                                                  ca.TryGetDateTime(out var cdt)
                                ? cdt
                                : DateTime.Now;

                            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                            try
                            {
                                if (pixelWidth <= 0 || pixelHeight <= 0)
                                    (pixelWidth, pixelHeight) = ReadImageDimensions(path);

                                BitmapImage thumb = ClipboardService.LoadThumbnail(path, pixelWidth);

                                shots.Add(new ScreenshotModel
                                {
                                    Name        = name,
                                    Path        = path,
                                    ContentHash = contentHash,
                                    SourceApp   = sourceApp,
                                    PixelWidth  = pixelWidth,
                                    PixelHeight = pixelHeight,
                                    CapturedAt  = capturedAt,
                                    Image       = thumb,
                                    OcrText     = ocrText,
                                    IsPinned    = pinned
                                });
                            }
                            catch (Exception ex) { Log.Warn($"Load screenshot image failed: {ex.Message}"); }
                        }
                    }
                }
                catch (Exception ex) { Log.Error("Session load failed", ex); }
            }

            return (texts, shots);
        }

        private static (int Width, int Height) ReadImageDimensions(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0)
                    return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
            }
            catch { }
            return (0, 0);
        }
    }
}
