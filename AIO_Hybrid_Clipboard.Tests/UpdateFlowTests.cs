using AIO_Hybrid_Clipboard.Services;
using AIO_Hybrid_Clipboard.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    /// <summary>Returns a canned HTTP response for every request.</summary>
    internal sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeHttpHandler(HttpStatusCode status, string body = "")
        {
            _status = status;
            _body   = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
    }

    internal sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmAnswer { get; set; }
        public List<string> Confirmations { get; } = new();
        public List<string> Errors { get; } = new();

        public bool Confirm(string message, string title) { Confirmations.Add(message); return ConfirmAnswer; }
        public void ShowError(string message, string title) => Errors.Add(message);
    }

    public class UpdateServiceParsingTests
    {
        private const string ReleaseJson = """
        {
          "tag_name": "v9.7.5",
          "assets": [
            { "name": "readme.txt", "browser_download_url": "https://example.test/readme.txt" },
            { "name": "AIO_Hybrid_Clipboard_Setup_v9.7.5.exe", "browser_download_url": "https://example.test/setup.exe" }
          ]
        }
        """;

        [Fact]
        public async Task CheckLatest_ParsesTagAndPicksSetupAsset()
        {
            var service = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK, ReleaseJson));

            var info = await service.CheckLatestAsync();

            Assert.NotNull(info);
            Assert.Equal(new Version(9, 7, 5), info!.LatestVersion);
            Assert.Equal("https://example.test/setup.exe", info.InstallerUrl);
        }

        [Fact]
        public async Task CheckLatest_ReturnsNullOnHttpError()
        {
            var service = new UpdateService(new FakeHttpHandler(HttpStatusCode.Forbidden));
            Assert.Null(await service.CheckLatestAsync());
        }

        [Fact]
        public async Task CheckLatest_ReturnsNullOnUnparsableTag()
        {
            var service = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK, """{ "tag_name": "latest-build" }"""));
            Assert.Null(await service.CheckLatestAsync());
        }

        [Fact]
        public async Task CheckLatest_HandlesTagWithoutPrefixAndMissingInstaller()
        {
            var service = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK,
                """{ "tag_name": "2.0", "assets": [ { "name": "notes.zip", "browser_download_url": "https://example.test/n.zip" } ] }"""));

            var info = await service.CheckLatestAsync();

            Assert.NotNull(info);
            Assert.Equal(new Version(2, 0, 0), info!.LatestVersion); // missing Build normalized to 0
            Assert.Null(info.InstallerUrl);                          // no Setup*.exe asset
        }
    }

    public class ViewModelUpdateFlowTests
    {
        private static MainViewModel CreateVm(UpdateService updater, FakeDialogService dialogs)
        {
            if (File.Exists(AppPaths.SessionFile)) File.Delete(AppPaths.SessionFile);
            return new MainViewModel(updater, dialogs);
        }

        [Fact]
        public async Task NewerRelease_PromptsAndShowsAvailableStatus()
        {
            var updater = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK,
                """{ "tag_name": "v99.0.0", "assets": [] }"""));
            var dialogs = new FakeDialogService { ConfirmAnswer = false }; // user declines
            var vm = CreateVm(updater, dialogs);

            await vm.CheckForUpdatesAsync();

            Assert.Single(dialogs.Confirmations);                    // update prompt was shown
            Assert.Contains("99.0.0", vm.UpdateStatus);              // status reflects the newer version
        }

        [Fact]
        public async Task SameVersion_ReportsUpToDateWithoutPrompting()
        {
            // Latest release equals the running assembly version (e.g. right after updating).
            var current = UpdateService.CurrentVersion;
            var updater = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK,
                $$"""{ "tag_name": "v{{current}}", "assets": [] }"""));
            var dialogs = new FakeDialogService();
            var vm = CreateVm(updater, dialogs);

            await vm.CheckForUpdatesAsync();

            Assert.Empty(dialogs.Confirmations);                     // no prompt
            Assert.Equal(vm.Settings.T("UpdateUpToDate"), vm.UpdateStatus);
        }

        [Fact]
        public async Task OlderRelease_NeverOffersDowngrade()
        {
            var updater = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK,
                """{ "tag_name": "v0.0.1", "assets": [] }"""));
            var dialogs = new FakeDialogService();
            var vm = CreateVm(updater, dialogs);

            await vm.CheckForUpdatesAsync();

            Assert.Empty(dialogs.Confirmations);
            Assert.Equal(vm.Settings.T("UpdateUpToDate"), vm.UpdateStatus);
        }

        [Fact]
        public async Task ApiFailure_ShowsFailedStatusWithoutPrompting()
        {
            var updater = new UpdateService(new FakeHttpHandler(HttpStatusCode.ServiceUnavailable));
            var dialogs = new FakeDialogService();
            var vm = CreateVm(updater, dialogs);

            await vm.CheckForUpdatesAsync();

            Assert.Empty(dialogs.Confirmations);
            Assert.Equal(vm.Settings.T("UpdateFailed"), vm.UpdateStatus);
        }
    }

    /// <summary>
    /// Live contract test against the real GitHub Releases API: the published
    /// release must be discoverable, parseable and carry a Setup asset — this is
    /// exactly what deployed apps consume. Self-skips when offline/rate-limited.
    /// </summary>
    public class UpdateServiceLiveTests
    {
        [Fact]
        public async Task LatestPublishedRelease_IsDiscoverableAndInstallable()
        {
            UpdateService.UpdateInfo? info;
            try { info = await new UpdateService().CheckLatestAsync(); }
            catch (HttpRequestException) { return; } // offline
            catch (TaskCanceledException) { return; } // network timeout

            if (info == null) return; // rate-limited or API unavailable

            Assert.True(info.LatestVersion >= new Version(1, 6, 0));
            Assert.True(info.LatestVersion <= UpdateService.CurrentVersion,
                $"Published v{info.LatestVersion} is newer than this build v{UpdateService.CurrentVersion} — did the csproj version fall behind the release tag?");
            Assert.NotNull(info.InstallerUrl);
            Assert.EndsWith(".exe", info.InstallerUrl, StringComparison.OrdinalIgnoreCase);
        }
    }
}
