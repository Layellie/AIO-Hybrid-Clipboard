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
        private readonly byte[]? _bytes;

        public FakeHttpHandler(HttpStatusCode status, string body = "")
        {
            _status = status;
            _body   = body;
        }

        public FakeHttpHandler(HttpStatusCode status, byte[] bytes)
        {
            _status = status;
            _body   = string.Empty;
            _bytes  = bytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HttpContent content = _bytes != null
                ? new ByteArrayContent(_bytes)
                : new StringContent(_body);
            return Task.FromResult(new HttpResponseMessage(_status) { Content = content });
        }
    }

    internal sealed class FakeDialogService : IDialogService
    {
        public bool    ConfirmAnswer { get; set; }
        public string? PromptAnswer  { get; set; }
        public string? SavePathAnswer { get; set; }
        public List<string> Confirmations { get; } = new();
        public List<string> Errors        { get; } = new();

        public bool Confirm(string message, string title) { Confirmations.Add(message); return ConfirmAnswer; }
        public void ShowError(string message, string title) => Errors.Add(message);

        public string? PromptText(string title, string prompt, string initialValue, bool multiline = false, string? okLabel = null, string? cancelLabel = null)
            => PromptAnswer;

        public string? PickSavePngPath(string suggestedFileName)
            => SavePathAnswer;
    }

    public class UpdateServiceParsingTests
    {
        private const string ReleaseJson = """
        {
          "tag_name": "v9.7.5",
          "assets": [
            { "name": "readme.txt", "browser_download_url": "https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v9.7.5/readme.txt" },
            { "name": "AIO_Hybrid_Clipboard_Setup_v9.7.5.exe", "browser_download_url": "https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v9.7.5/AIO_Hybrid_Clipboard_Setup_v9.7.5.exe" }
          ]
        }
        """;

        [Fact]
        public async Task CheckLatest_ParsesTagAndPicksTrustedSetupAsset()
        {
            var service = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK, ReleaseJson));

            var info = await service.CheckLatestAsync();

            Assert.NotNull(info);
            Assert.Equal(new Version(9, 7, 5), info!.LatestVersion);
            Assert.Equal(
                "https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v9.7.5/AIO_Hybrid_Clipboard_Setup_v9.7.5.exe",
                info.InstallerUrl);
        }

        [Fact]
        public async Task CheckLatest_RejectsUntrustedInstallerHost()
        {
            const string maliciousJson = """
            {
              "tag_name": "v9.9.9",
              "assets": [
                { "name": "AIO_Hybrid_Clipboard_Setup_v9.9.9.exe", "browser_download_url": "https://evil.example.com/Setup.exe" }
              ]
            }
            """;

            var service = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK, maliciousJson));
            var info = await service.CheckLatestAsync();

            Assert.NotNull(info);
            Assert.Null(info!.InstallerUrl);
        }

        [Theory]
        [InlineData("https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v2.0.0/AIO_Setup.exe", true)]
        [InlineData("https://objects.githubusercontent.com/github-production-release-asset/AIO_Setup.exe", true)]
        [InlineData("https://release-assets.githubusercontent.com/assets/Setup_v2.exe", true)]
        [InlineData("http://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v2.0.0/AIO_Setup.exe", false)]
        [InlineData("https://evilgithub.com/releases/Setup.exe", false)]
        [InlineData("https://github.com.evil.com/releases/Setup.exe", false)]
        [InlineData("https://github.com/Layellie/repo/releases/../../escape.exe", false)]
        [InlineData("https://github.com/Layellie/repo/releases/%2e%2e%5cevil.exe", false)]
        [InlineData("https://github.com/Layellie/repo/releases/archive.zip", false)]
        [InlineData("", false)]
        public void IsTrustedDownloadUrl_EnforcesHttpsGitHubHostAndSafeFilename(string url, bool expected)
        {
            Assert.Equal(expected, UpdateService.IsTrustedDownloadUrl(url));
        }

        [Fact]
        public async Task DownloadInstallerAsync_ThrowsOnUntrustedUrl()
        {
            var service = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK, new byte[] { 1, 2, 3 }));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DownloadInstallerAsync("http://evil.com/AIO_Setup.exe"));
        }

        [Fact]
        public async Task DownloadInstallerAsync_WritesFileAndReportsProgress()
        {
            byte[] payload = new byte[4096];
            Array.Fill(payload, (byte)0x5A);

            var service = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK, payload));
            string fileName = $"AIO_Test_Setup_{Guid.NewGuid():N}.exe";
            string url = $"https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v2.0.0/{fileName}";

            double lastReported = 0;
            var progress = new SyncProgress<double>(p => lastReported = p);

            string? path = null;
            try
            {
                path = await service.DownloadInstallerAsync(url, progress);

                Assert.True(File.Exists(path));
                Assert.False(File.Exists(path + ".part"));
                Assert.Equal(payload.Length, new FileInfo(path).Length);
                Assert.Equal(100.0, lastReported);
            }
            finally
            {
                if (path != null && File.Exists(path)) File.Delete(path);
            }
        }

        private sealed class SyncProgress<T>(Action<T> callback) : IProgress<T>
        {
            public void Report(T value) => callback(value);
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
                """{ "tag_name": "2.0", "assets": [ { "name": "notes.zip", "browser_download_url": "https://github.com/Layellie/repo/n.zip" } ] }"""));

            var info = await service.CheckLatestAsync();

            Assert.NotNull(info);
            Assert.Equal(new Version(2, 0, 0), info!.LatestVersion);
            Assert.Null(info.InstallerUrl);
        }
    }

    public class ViewModelUpdateFlowTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly IDisposable _scope;

        public ViewModelUpdateFlowTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "aio-upd-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _scope = AppPaths.OverrideCacheFolder(_tempDir);
        }

        public void Dispose()
        {
            _scope.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { }
        }

        private MainViewModel CreateVm(UpdateService updater, FakeDialogService dialogs)
        {
            if (File.Exists(AppPaths.SessionFile)) File.Delete(AppPaths.SessionFile);
            var settings = new SettingsService(@"SOFTWARE\AIO_ClipboardSearch_UpdTests_" + Guid.NewGuid().ToString("N"));
            return new MainViewModel(updater, dialogs, settings) { AutoSaveEnabled = false };
        }

        [Fact]
        public async Task NewerRelease_PromptsAndShowsAvailableStatus()
        {
            var updater = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK,
                """{ "tag_name": "v99.0.0", "assets": [] }"""));
            var dialogs = new FakeDialogService { ConfirmAnswer = false };
            var vm = CreateVm(updater, dialogs);

            await vm.CheckForUpdatesAsync();

            Assert.Single(dialogs.Confirmations);
            Assert.Contains("99.0.0", vm.UpdateStatus);
        }

        [Fact]
        public async Task SameVersion_ReportsUpToDateWithoutPrompting()
        {
            var current = UpdateService.CurrentVersion;
            var updater = new UpdateService(new FakeHttpHandler(HttpStatusCode.OK,
                $$"""{ "tag_name": "v{{current}}", "assets": [] }"""));
            var dialogs = new FakeDialogService();
            var vm = CreateVm(updater, dialogs);

            await vm.CheckForUpdatesAsync();

            Assert.Empty(dialogs.Confirmations);
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

    public class UpdateServiceLiveTests
    {
        [Fact]
        public async Task LatestPublishedRelease_IsDiscoverableAndInstallable()
        {
            UpdateService.UpdateInfo? info;
            try { info = await new UpdateService().CheckLatestAsync(); }
            catch (HttpRequestException) { return; }
            catch (TaskCanceledException) { return; }

            if (info == null) return;

            Assert.True(info.LatestVersion >= new Version(1, 6, 0));
            Assert.True(info.LatestVersion <= UpdateService.CurrentVersion,
                $"Published v{info.LatestVersion} is newer than this build v{UpdateService.CurrentVersion} — did the csproj version fall behind the release tag?");
            Assert.NotNull(info.InstallerUrl);
            Assert.EndsWith(".exe", info.InstallerUrl, StringComparison.OrdinalIgnoreCase);
        }
    }
}
