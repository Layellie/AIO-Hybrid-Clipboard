using AIO_Hybrid_Clipboard.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    public class ClipboardPrivacyGuardTests
    {
        [Theory]
        [InlineData(ClipboardPrivacyGuard.ExcludeMonitorFormat)]
        [InlineData(ClipboardPrivacyGuard.ClipboardViewerIgnoreFormat)]
        [InlineData(ClipboardPrivacyGuard.ConcealedTypeFormat)]
        [InlineData(ClipboardPrivacyGuard.TransientTypeFormat)]
        public void ShouldBlock_ReturnsTrueForPresenceExclusionFormats(string format)
        {
            bool blocked = ClipboardPrivacyGuard.ShouldBlock(
                hasFormat: f => f == format,
                readDword: _ => null);

            Assert.True(blocked);
        }

        [Fact]
        public void ShouldBlock_ReturnsTrueWhenCanIncludeInClipboardHistoryIsZero()
        {
            bool blocked = ClipboardPrivacyGuard.ShouldBlock(
                hasFormat: f => f == ClipboardPrivacyGuard.CanIncludeInHistoryFormat,
                readDword: _ => 0);

            Assert.True(blocked);
        }

        [Fact]
        public void ShouldBlock_ReturnsFalseWhenCanIncludeInClipboardHistoryIsOne()
        {
            bool blocked = ClipboardPrivacyGuard.ShouldBlock(
                hasFormat: f => f == ClipboardPrivacyGuard.CanIncludeInHistoryFormat,
                readDword: _ => 1);

            Assert.False(blocked);
        }

        [Fact]
        public void ShouldBlock_ReturnsFalseForNormalClipboard()
        {
            bool blocked = ClipboardPrivacyGuard.ShouldBlock(
                hasFormat: _ => false,
                readDword: _ => null);

            Assert.False(blocked);
        }
    }

    public class LocalizationParityTests
    {
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AIO Hybrid Clipboard.slnx")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repository root containing AIO Hybrid Clipboard.slnx.");
        }

        private static Dictionary<string, string> ReadResxEntries(string path)
        {
            var doc = XDocument.Load(path);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var data in doc.Root!.Elements("data"))
            {
                string? key = data.Attribute("name")?.Value;
                string? val = data.Element("value")?.Value;
                Assert.False(string.IsNullOrWhiteSpace(key), $"Empty key found in {path}");
                Assert.False(map.ContainsKey(key!), $"Duplicate key '{key}' found in {path}");
                map[key!] = val ?? string.Empty;
            }
            return map;
        }

        [Fact]
        public void EnglishAndTurkishResx_HaveExactKeyParityAndNonEmptyValues()
        {
            string root   = FindRepoRoot();
            string enPath = Path.Combine(root, "AIO Clipboard & Search", "Resources", "Strings.resx");
            string trPath = Path.Combine(root, "AIO Clipboard & Search", "Resources", "Strings.tr.resx");

            var en = ReadResxEntries(enPath);
            var tr = ReadResxEntries(trPath);

            var missingInTr = en.Keys.Except(tr.Keys).ToList();
            var missingInEn = tr.Keys.Except(en.Keys).ToList();

            Assert.True(missingInTr.Count == 0, $"Keys missing in Strings.tr.resx: {string.Join(", ", missingInTr)}");
            Assert.True(missingInEn.Count == 0, $"Keys missing in Strings.resx: {string.Join(", ", missingInEn)}");

            foreach (var (key, value) in en)
                Assert.False(string.IsNullOrWhiteSpace(value), $"Empty English value for key '{key}'");

            foreach (var (key, value) in tr)
                Assert.False(string.IsNullOrWhiteSpace(value), $"Empty Turkish value for key '{key}'");
        }

        [Fact]
        public void DarkTheme_FreezesAllBrushesAndGeometries()
        {
            string root      = FindRepoRoot();
            string themePath = Path.Combine(root, "AIO Clipboard & Search", "Themes", "DarkTheme.xaml");
            var doc = XDocument.Load(themePath);

            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace po   = "http://schemas.microsoft.com/winfx/2006/xaml/presentation/options";

            var freezables = doc.Root!.Elements()
                .Where(e => e.Name == xaml + "SolidColorBrush" || e.Name == xaml + "Geometry")
                .ToList();

            Assert.NotEmpty(freezables);
            foreach (var el in freezables)
            {
                string? freezeAttr = el.Attribute(po + "Freeze")?.Value;
                Assert.True(
                    string.Equals(freezeAttr, "True", StringComparison.OrdinalIgnoreCase),
                    $"Resource '{el.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value}' is missing po:Freeze=\"True\"");
            }
        }
    }

    public class PerformanceAndStabilityTests
    {
        [Fact]
        public void HugeTextOver1MB_BoundsPreviewTooltipAndLineScanFast()
        {
            // 1.5M chars (~3 MB UTF-16)
            string huge = new string('x', 1_500_000);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var item = new AIO_Hybrid_Clipboard.Models.ClipItem(huge);

            sw.Stop();
            Assert.True(item.IsLargeText);
            Assert.True(item.PreviewText.Length <= 160);
            Assert.True(item.TooltipText.Length <= 4001);
            Assert.Equal(1, item.LineCount);
            Assert.True(sw.ElapsedMilliseconds < 250, $"Huge text classification took too long: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void PooledPixelBuffer_DisposeIsIdempotentAndPreventsUseAfterFree()
        {
            byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(1024);
            var pooled = new PooledPixelBuffer(rented, 1024, 16, 16, 64);

            Assert.Same(rented, pooled.Buffer);
            pooled.Dispose();
            pooled.Dispose(); // idempotent

            Assert.Throws<ObjectDisposedException>(() => _ = pooled.Buffer);
        }
    }
}
