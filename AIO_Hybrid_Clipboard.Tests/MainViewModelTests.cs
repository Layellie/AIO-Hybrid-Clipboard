using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using AIO_Hybrid_Clipboard.ViewModels;
using System.Collections;
using System.IO;
using System.Linq;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    public class MainViewModelTests
    {
        private static MainViewModel CreateVm()
        {
            // Fresh session state so constructor seeding is deterministic.
            if (File.Exists(AppPaths.SessionFile)) File.Delete(AppPaths.SessionFile);
            return new MainViewModel();
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
        public void CopyText_RaisesHideRequested_WhenHideOnCopyEnabled()
        {
            var vm = CreateVm();
            vm.HideOnCopy = true;
            bool raised = false;
            vm.HideRequested += () => raised = true;

            vm.CopyText("hello"); // no clipboard attached — copy is a no-op, hide still fires

            Assert.True(raised);
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
    }
}
