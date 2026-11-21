using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using System;
using System.IO;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    public class SettingsServiceTests
    {
        [Fact]
        public void T_ReturnsEnglishByDefault()
        {
            var settings = new SettingsService();
            Assert.Equal("Start with Windows", settings.T("StartWithWindows"));
        }

        [Fact]
        public void T_ReturnsTurkishWhenSelected()
        {
            var settings = new SettingsService { CurrentLanguage = SettingsService.AppLanguage.Turkish };
            Assert.Equal("Windows ile başlat", settings.T("StartWithWindows"));
        }

        [Fact]
        public void T_FallsBackToKeyForUnknownEntries()
        {
            var settings = new SettingsService();
            Assert.Equal("NoSuchKey", settings.T("NoSuchKey"));
        }

        [Fact]
        public void StartupService_FormatsQuotedPathWithMinimizedFlag()
        {
            string cmd = StartupService.FormatStartupCommand(@"C:\Program Files\AIO Hybrid Clipboard\AIO_Hybrid_Clipboard.exe");
            Assert.Equal("\"C:\\Program Files\\AIO Hybrid Clipboard\\AIO_Hybrid_Clipboard.exe\" --minimized", cmd);
        }
    }

    public class UpdateServiceTests
    {
        [Fact]
        public void Normalize_DropsRevisionComponent()
        {
            Assert.Equal(new Version(1, 5, 0), UpdateService.Normalize(new Version(1, 5, 0, 0)));
        }

        [Fact]
        public void Normalize_FillsMissingComponentsWithZero()
        {
            Assert.Equal(new Version(2, 0, 0), UpdateService.Normalize(new Version(2, 0)));
        }

        [Fact]
        public void Normalize_MakesTagAndAssemblyVersionsComparable()
        {
            var tag      = UpdateService.Normalize(new Version("2.0.0"));
            var assembly = UpdateService.Normalize(new Version("2.0.0.0"));
            Assert.Equal(0, tag.CompareTo(assembly));
        }
    }

    public class OcrPlaceholderTests
    {
        [Theory]
        [InlineData("[OCR: No readable text found in the image]")]
        [InlineData("[OCR Error: Windows OCR engine failed to initialize]")]
        public void IsPlaceholder_MatchesLegacyEngineMarkers(string text)
        {
            Assert.True(OcrService.IsPlaceholder(text));
        }

        [Fact]
        public void IsPlaceholder_IgnoresRegularText()
        {
            Assert.False(OcrService.IsPlaceholder("regular clipboard text"));
        }
    }

    public class SessionStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly IDisposable _scope;

        public SessionStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "aio-session-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _scope = AppPaths.OverrideCacheFolder(_tempDir);
        }

        public void Dispose()
        {
            _scope.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Fact]
        public void SaveThenLoad_RoundtripsTextsPinsAndTimestampsAtomically()
        {
            var created = new DateTime(2026, 11, 20, 14, 30, 0);
            var texts = new[]
            {
                new ClipItem("pinned entry", created) { IsPinned = true },
                new ClipItem("plain entry"),
            };

            SessionStore.Save(texts, Array.Empty<ScreenshotModel>());

            Assert.True(File.Exists(AppPaths.SessionFile));
            Assert.False(File.Exists(AppPaths.SessionFile + ".tmp")); // atomic move cleaned up .tmp

            var (loaded, shots) = SessionStore.Load();

            Assert.Empty(shots);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("pinned entry", loaded[0].Text);
            Assert.True(loaded[0].IsPinned);
            Assert.Equal(created, loaded[0].CreatedAt);
            Assert.False(loaded[1].IsPinned);
        }

        [Fact]
        public void Load_ReadsLegacyPlainStringEntries()
        {
            AppPaths.EnsureCacheFolder();
            File.WriteAllText(AppPaths.SessionFile,
                """{"ClipboardEntries":["old plain string"],"Screenshots":[]}""");

            var (loaded, _) = SessionStore.Load();

            Assert.Single(loaded);
            Assert.Equal("old plain string", loaded[0].Text);
            Assert.False(loaded[0].IsPinned);
        }

        [Fact]
        public void Load_SkipsScreenshotsWhoseFileIsGone()
        {
            AppPaths.EnsureCacheFolder();
            File.WriteAllText(AppPaths.SessionFile,
                """{"ClipboardEntries":[],"Screenshots":[{"Name":"ghost","Path":"Z:\\nonexistent\\ghost.png","OcrText":"","IsPinned":false}]}""");

            var (_, shots) = SessionStore.Load();

            Assert.Empty(shots);
        }
    }
}
