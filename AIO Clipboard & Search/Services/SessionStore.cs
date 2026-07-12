using AIO_Hybrid_Clipboard.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace AIO_Hybrid_Clipboard.Services
{
    internal static class SessionStore
    {
        private static string FilePath => AppPaths.SessionFile;

        public static void Save(IEnumerable<ClipItem> texts, IEnumerable<ScreenshotModel> screenshots)
        {
            try
            {
                AppPaths.EnsureCacheFolder();

                var data = new
                {
                    ClipboardEntries = texts.Select(c => new { c.Text, c.IsPinned }).ToList(),
                    Screenshots = screenshots.Select(s => new { s.Name, s.Path, s.OcrText, s.IsPinned }).ToList()
                };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
            }
            catch (Exception ex) { Debug.WriteLine($"[AIO] Session save failed: {ex.Message}"); }
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
                    catch (Exception ex) { Debug.WriteLine($"[AIO] Orphan cache delete failed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[AIO] Cache cleanup failed: {ex.Message}"); }
        }

        public static (List<ClipItem> texts, List<ScreenshotModel> screenshots) Load()
        {
            var texts = new List<ClipItem>();
            var shots = new List<ScreenshotModel>();
            try
            {
                if (!File.Exists(FilePath)) return (texts, shots);

                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                var root = doc.RootElement;

                if (root.TryGetProperty("ClipboardEntries", out var entries))
                    foreach (var entry in entries.EnumerateArray())
                    {
                        // Back-compat: old sessions stored plain strings.
                        if (entry.ValueKind == JsonValueKind.String)
                        {
                            string? legacy = entry.GetString();
                            if (!string.IsNullOrEmpty(legacy)) texts.Add(new ClipItem(legacy));
                            continue;
                        }

                        string? t   = entry.TryGetProperty("Text", out var tv) ? tv.GetString() : null;
                        bool pinned = entry.TryGetProperty("IsPinned", out var pv) && pv.ValueKind == JsonValueKind.True;
                        if (!string.IsNullOrEmpty(t)) texts.Add(new ClipItem(t) { IsPinned = pinned });
                    }

                if (root.TryGetProperty("Screenshots", out var screenshots))
                    foreach (var s in screenshots.EnumerateArray())
                    {
                        string name    = s.TryGetProperty("Name",    out var n) ? n.GetString() ?? "" : "";
                        string path    = s.TryGetProperty("Path",    out var p) ? p.GetString() ?? "" : "";
                        string ocrText = s.TryGetProperty("OcrText", out var o) ? o.GetString() ?? "" : "";
                        bool   pinned  = s.TryGetProperty("IsPinned", out var sp) && sp.ValueKind == JsonValueKind.True;

                        if (!File.Exists(path)) continue;
                        try
                        {
                            var img = new BitmapImage();
                            img.BeginInit();
                            img.CacheOption = BitmapCacheOption.OnLoad;
                            img.UriSource   = new Uri(path, UriKind.Absolute);
                            img.EndInit();
                            img.Freeze();
                            shots.Add(new ScreenshotModel { Name = name, Path = path, Image = img, OcrText = ocrText, IsPinned = pinned });
                        }
                        catch (Exception ex) { Debug.WriteLine($"[AIO] Load screenshot image failed: {ex.Message}"); }
                    }
            }
            catch (Exception ex) { Debug.WriteLine($"[AIO] Session load failed: {ex.Message}"); }
            return (texts, shots);
        }
    }
}
