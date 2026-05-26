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
        private static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache", "session.json");

        public static void Save(IEnumerable<string> texts, IEnumerable<ScreenshotModel> screenshots)
        {
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var data = new
                {
                    ClipboardEntries = texts.ToList(),
                    Screenshots = screenshots.Select(s => new { s.Name, s.Path, s.OcrText }).ToList()
                };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
            }
            catch (Exception ex) { Debug.WriteLine($"[AIO] Session save failed: {ex.Message}"); }
        }

        public static (List<string> texts, List<ScreenshotModel> screenshots) Load()
        {
            var texts = new List<string>();
            var shots = new List<ScreenshotModel>();
            try
            {
                if (!File.Exists(FilePath)) return (texts, shots);

                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                var root = doc.RootElement;

                if (root.TryGetProperty("ClipboardEntries", out var entries))
                    foreach (var entry in entries.EnumerateArray())
                    {
                        string? t = entry.GetString();
                        if (!string.IsNullOrEmpty(t)) texts.Add(t);
                    }

                if (root.TryGetProperty("Screenshots", out var screenshots))
                    foreach (var s in screenshots.EnumerateArray())
                    {
                        string name    = s.TryGetProperty("Name",    out var n) ? n.GetString() ?? "" : "";
                        string path    = s.TryGetProperty("Path",    out var p) ? p.GetString() ?? "" : "";
                        string ocrText = s.TryGetProperty("OcrText", out var o) ? o.GetString() ?? "" : "";

                        if (!File.Exists(path)) continue;
                        try
                        {
                            var img = new BitmapImage();
                            img.BeginInit();
                            img.CacheOption = BitmapCacheOption.OnLoad;
                            img.UriSource   = new Uri(path, UriKind.Absolute);
                            img.EndInit();
                            img.Freeze();
                            shots.Add(new ScreenshotModel { Name = name, Path = path, Image = img, OcrText = ocrText });
                        }
                        catch (Exception ex) { Debug.WriteLine($"[AIO] Load screenshot image failed: {ex.Message}"); }
                    }
            }
            catch (Exception ex) { Debug.WriteLine($"[AIO] Session load failed: {ex.Message}"); }
            return (texts, shots);
        }
    }
}
