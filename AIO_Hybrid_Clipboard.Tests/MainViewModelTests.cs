using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using AIO_Hybrid_Clipboard.ViewModels;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    public class MainViewModelTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly IDisposable _cacheScope;

        public MainViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "aio-vm-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _cacheScope = AppPaths.OverrideCacheFolder(_tempDir);
        }

        public void Dispose()
        {
            _cacheScope.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        private MainViewModel CreateVm(FakeDialogService? dialogs = null, int searchDebounceMs = 0, bool loadSessionSync = true)
        {
            if (loadSessionSync && File.Exists(AppPaths.SessionFile)) File.Delete(AppPaths.SessionFile);
            var settings = new SettingsService(@"SOFTWARE\AIO_ClipboardSearch_Tests_" + Guid.NewGuid().ToString("N"));
            var vm = new MainViewModel(null, dialogs ?? new FakeDialogService(), settings, loadSessionSync: loadSessionSync)
            {
                AutoSaveEnabled  = false,
                SearchDebounceMs = searchDebounceMs
            };
            return vm;
        }

        [Fact]
        public void SearchQuery_FiltersTextsView()
        {
            var vm = CreateVm();
            vm.History.InsertText("alpha entry");
            vm.History.InsertText("beta entry");

            vm.SearchQuery = "alpha";

            var visible = vm.TextsView.Cast<ClipItem>().ToList();
            Assert.Single(visible);
            Assert.Equal("alpha entry", visible[0].Text);

            vm.SearchQuery = "";
            Assert.True(vm.TextsView.Cast<ClipItem>().Count() >= 2);
        }

        [Fact]
        public void SearchQuery_PreservesSpacesForMultiWordTyping()
        {
            var vm = CreateVm();
            vm.History.InsertText("Hello big World");
            vm.History.InsertText("Hello only");

            // Regression: typing a trailing space must NOT be swallowed by Trim() in the setter.
            vm.SearchQuery = "hello ";
            Assert.Equal("hello ", vm.SearchQuery);

            vm.SearchQuery = "hello world";
            var visible = vm.TextsView.Cast<ClipItem>().ToList();
            Assert.Single(visible);
            Assert.Equal("Hello big World", visible[0].Text);
        }

        [Fact]
        public void SearchQuery_FiltersScreenshotsByNameAndOcrText()
        {
            var vm = CreateVm();
            vm.History.AddScreenshot(new ScreenshotModel { Name = "Capture - 10:00", OcrText = "invoice total" });
            vm.History.AddScreenshot(new ScreenshotModel { Name = "Capture - 11:00", OcrText = "meeting notes" });

            vm.SearchQuery = "invoice";
            Assert.Single(vm.ScreenshotsView.Cast<ScreenshotModel>());

            vm.SearchQuery = "capture";
            Assert.Equal(2, vm.ScreenshotsView.Cast<ScreenshotModel>().Count());
        }

        [Fact]
        public void CategoryFilters_FilterByPinnedLinksCodeAndColors()
        {
            var vm = CreateVm();
            vm.History.Texts.Clear();

            vm.History.InsertText("https://github.com/Layellie/AIO-Hybrid-Clipboard");
            vm.History.InsertText("#5865F2");
            vm.History.InsertText("dotnet build --configuration Release");
            vm.History.InsertText("plain meeting note");
            vm.History.TogglePin(vm.History.Texts.First(t => t.Text == "plain meeting note"));

            var pinShot = new ScreenshotModel { Name = "pinned-shot" };
            var regShot = new ScreenshotModel { Name = "regular-shot" };
            vm.History.AddScreenshot(pinShot);
            vm.History.TogglePin(pinShot);
            vm.History.AddScreenshot(regShot);

            vm.SelectedCategory = ClipFilterCategory.Links;
            Assert.Single(vm.TextsView.Cast<ClipItem>());
            Assert.Equal(ClipKind.Url, vm.TextsView.Cast<ClipItem>().First().Kind);

            vm.SelectedCategory = ClipFilterCategory.Colors;
            Assert.Single(vm.TextsView.Cast<ClipItem>());
            Assert.Equal("#5865F2", vm.TextsView.Cast<ClipItem>().First().ColorHex);

            vm.SelectedCategory = ClipFilterCategory.Code;
            Assert.Single(vm.TextsView.Cast<ClipItem>());
            Assert.Equal(ClipKind.Code, vm.TextsView.Cast<ClipItem>().First().Kind);

            vm.SelectedCategory = ClipFilterCategory.Pinned;
            Assert.Single(vm.TextsView.Cast<ClipItem>());
            Assert.Single(vm.ScreenshotsView.Cast<ScreenshotModel>());

            vm.SelectedCategory = ClipFilterCategory.All;
            Assert.Equal(4, vm.TextsView.Cast<ClipItem>().Count());
            Assert.Equal(2, vm.ScreenshotsView.Cast<ScreenshotModel>().Count());
        }

        [Fact]
        public void CopyText_RaisesHideRequested_WhenHideOnCopyEnabled()
        {
            var vm = CreateVm();
            vm.HideOnCopy = true;
            vm.AutoPaste  = false;
            bool raised = false;
            vm.HideRequested += () => raised = true;

            vm.CopyText("hello"); // no clipboard attached — copy is a no-op, hide still fires

            Assert.True(raised);
        }

        [Fact]
        public void CopyText_RaisesAutoPasteAndHide_WhenAutoPasteEnabledOrForced()
        {
            var vm = CreateVm();
            vm.HideOnCopy = false;
            vm.AutoPaste  = false;

            bool hidden = false, pasted = false;
            vm.HideRequested                  += () => hidden = true;
            vm.PasteToPreviousWindowRequested += () => pasted = true;

            vm.CopyText("hello", forceAutoPaste: true);

            Assert.True(hidden);
            Assert.True(pasted);
        }

        [Fact]
        public void CopyText_IgnoresLegacyPlaceholdersAndEmpty()
        {
            var vm = CreateVm();
            vm.HideOnCopy = true;
            bool raised = false;
            vm.HideRequested += () => raised = true;

            vm.CopyText("[OCR: No readable text found in the image]");
            vm.CopyText("");
            vm.CopyText(null);

            Assert.False(raised);
        }

        [Fact]
        public void CopyText_DoesNothingInEditMode()
        {
            var vm = CreateVm();
            vm.HideOnCopy = true;
            vm.IsEditMode = true;
            bool raised = false;
            vm.HideRequested += () => raised = true;

            vm.CopyText("hello");

            Assert.False(raised);
        }

        [Fact]
        public void IsPaused_ShowsWarningToastAndUpdatesLabel()
        {
            var vm = CreateVm();
            Assert.False(vm.IsPaused);

            vm.IsPaused = true;

            Assert.True(vm.IsToastVisible);
            Assert.True(vm.ToastIsWarning);
            Assert.Equal(vm.Settings.T("ToastPaused"), vm.ToastMessage);
            Assert.Equal(vm.Settings.T("TrayResume"), vm.PauseMenuLabel);
        }

        [Fact]
        public void ClearUnpinned_RespectsConfirmationAndPreservesPins()
        {
            var dialogs = new FakeDialogService { ConfirmAnswer = false };
            var vm = CreateVm(dialogs);
            vm.History.Texts.Clear();
            vm.History.InsertText("keep-pin");
            vm.History.TogglePin(vm.History.Texts[0]);
            vm.History.InsertText("drop-me");

            vm.ClearUnpinnedCommand.Execute(null);
            Assert.Equal(2, vm.History.Texts.Count); // declined

            dialogs.ConfirmAnswer = true;
            vm.ClearUnpinnedCommand.Execute(null);
            Assert.Single(vm.History.Texts);
            Assert.Equal("keep-pin", vm.History.Texts[0].Text);
        }

        [Fact]
        public void DeleteItems_RemovesSelectedEntries()
        {
            var vm = CreateVm();
            vm.History.InsertText("keep");
            vm.History.InsertText("remove");
            var shot = new ScreenshotModel { Name = "shot", Path = Path.Combine(Path.GetTempPath(), "nonexistent-aio-test.png") };
            vm.History.AddScreenshot(shot);

            var doomedText = vm.History.Texts.First(t => t.Text == "remove");
            vm.DeleteItems(new ArrayList { doomedText }, new ArrayList { shot });

            Assert.DoesNotContain(vm.History.Texts, t => t.Text == "remove");
            Assert.Empty(vm.History.Screenshots);
        }

        [Fact]
        public void Loc_ReflectsSelectedLanguage()
        {
            var vm = CreateVm();

            vm.Settings.CurrentLanguage = SettingsService.AppLanguage.Turkish;
            Assert.Equal("Windows ile başlat", vm.Loc["StartWithWindows"]);

            vm.Settings.CurrentLanguage = SettingsService.AppLanguage.English;
            Assert.Equal("Start with Windows", vm.Loc["StartWithWindows"]);
        }

        [Fact]
        public void VersionText_MatchesAssemblyVersion()
        {
            var vm = CreateVm();
            Assert.Equal($"v{UpdateService.CurrentVersion}", vm.VersionText);
        }

        [Fact]
        public async System.Threading.Tasks.Task SearchQuery_DebouncesRapidTypingToSingleRefresh()
        {
            var vm = CreateVm(searchDebounceMs: 80);
            vm.History.InsertText("alpha entry");
            vm.History.InsertText("beta entry");

            int baseline = vm.FilterRefreshCount;

            // Rapid typing bursts within 80ms should collapse into one ICollectionView.Refresh().
            vm.SearchQuery = "a";
            vm.SearchQuery = "al";
            vm.SearchQuery = "alp";
            vm.SearchQuery = "alph";
            vm.SearchQuery = "alpha";

            Assert.Equal(baseline, vm.FilterRefreshCount);

            await System.Threading.Tasks.Task.Delay(160);

            Assert.Equal(baseline + 1, vm.FilterRefreshCount);
            Assert.Single(vm.TextsView.Cast<ClipItem>());
        }

        [Fact]
        public async System.Threading.Tasks.Task InitializeAsync_DefersSessionLoadUntilCalled()
        {
            SessionStore.Save(new[] { new ClipItem("persisted entry") }, Array.Empty<ScreenshotModel>());

            var vm = CreateVm(loadSessionSync: false);
            Assert.Empty(vm.History.Texts); // Constructor did zero blocking disk I/O.

            // Cancel after loading to prevent deferred 2s update delay from keeping test waiting.
            var initTask = vm.InitializeAsync();
            for (int i = 0; i < 40 && vm.History.Texts.Count == 0; i++)
                await System.Threading.Tasks.Task.Delay(25);

            Assert.Contains(vm.History.Texts, t => t.Text == "persisted entry");
            vm.CancelBackgroundWork();
            await initTask;
        }

        [Fact]
        public void ReleaseTransientResources_DropsHoverPreviewsAndStopsToast()
        {
            var vm = CreateVm();
            var thumb = System.Windows.Media.Imaging.BitmapSource.Create(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, new byte[4], 4);
            thumb.Freeze();
            var preview = System.Windows.Media.Imaging.BitmapSource.Create(
                2, 2, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, new byte[16], 8);
            preview.Freeze();

            var shot = new ScreenshotModel { Name = "s", Image = thumb, PreviewImage = preview };
            vm.History.AddScreenshot(shot);
            vm.ShowToast("hello", durationMs: 10000);

            Assert.Same(preview, shot.DisplayPreviewImage);
            Assert.True(vm.IsToastVisible);

            vm.ReleaseTransientResources();

            Assert.Null(shot.PreviewImage);
            Assert.Same(thumb, shot.DisplayPreviewImage);
            Assert.False(vm.IsToastVisible);
        }
    }
}
