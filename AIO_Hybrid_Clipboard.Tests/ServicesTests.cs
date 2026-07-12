using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
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
    }

    public class UpdateServiceTests
    {
        [Fact]
        public void Normalize_DropsRevisionComponent()
        {
            Assert.Equal(new System.Version(1, 5, 0), UpdateService.Normalize(new System.Version(1, 5, 0, 0)));
        }

        [Fact]
        public void Normalize_FillsMissingComponentsWithZero()
        {
            Assert.Equal(new System.Version(2, 0, 0), UpdateService.Normalize(new System.Version(2, 0)));
        }

        [Fact]
        public void Normalize_MakesTagAndAssemblyVersionsComparable()
        {
            var tag      = UpdateService.Normalize(new System.Version("1.5.0"));
            var assembly = UpdateService.Normalize(new System.Version("1.5.0.0"));
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

    // Session persistence roundtrip. These tests share the session.json path
    // (derived from the test host's base directory), so they live in one class
    // to run sequentially.
    public class SessionStoreTests
    {
        [Fact]
        public void SaveThenLoad_RoundtripsTextsAndPins()
        {
            var texts = new[]
            {
                new ClipItem("pinned entry") { IsPinned = true },
                new ClipItem("plain entry"),
            };

            SessionStore.Save(texts, System.Array.Empty<ScreenshotModel>());
            var (loaded, shots) = SessionStore.Load();

            Assert.Empty(shots);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("pinned entry", loaded[0].Text);
            Assert.True(loaded[0].IsPinned);
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
