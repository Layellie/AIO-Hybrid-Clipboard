using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Checks GitHub Releases for a newer version and runs the installer.
    /// Instance-based with an injectable <see cref="HttpMessageHandler"/> so the
    /// parsing and version logic are unit-testable without touching the network.
    /// </summary>
    internal class UpdateService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/Layellie/AIO-Hybrid-Clipboard/releases/latest";
        private const string ReleasesPage     = "https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/latest";

        private readonly HttpMessageHandler? _handler;

        public UpdateService(HttpMessageHandler? handler = null) => _handler = handler;

        internal sealed record UpdateInfo(Version LatestVersion, string? InstallerUrl);

        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return Normalize(v);
            }
        }

        // Assembly versions carry a 4th (revision) component and release tags don't;
        // compare everything as Major.Minor.Build.
        internal static Version Normalize(Version v) =>
            new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

        /// <summary>Returns the latest release info, or null when it cannot be determined.</summary>
        public async Task<UpdateInfo?> CheckLatestAsync()
        {
            using var http = CreateClient(TimeSpan.FromSeconds(30));
            using var response = await http.GetAsync(LatestReleaseApi);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return null;

            string? installerUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name != null &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                        asset.TryGetProperty("browser_download_url", out var url))
                    {
                        installerUrl = url.GetString();
                        break;
                    }
                }
            }

            return new UpdateInfo(Normalize(latest), installerUrl);
        }

        /// <summary>Downloads the installer to %TEMP% and returns its path.</summary>
        public async Task<string> DownloadInstallerAsync(string url)
        {
            // Generous timeout: the self-contained installer is tens of megabytes.
            using var http = CreateClient(TimeSpan.FromMinutes(10));
            string target = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(url).LocalPath));

            using var stream = await http.GetStreamAsync(url);
            using var file = new FileStream(target, FileMode.Create);
            await stream.CopyToAsync(file);
            return target;
        }

        public void RunInstaller(string path) =>
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        public void OpenReleasesPage() =>
            Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });

        private HttpClient CreateClient(TimeSpan timeout)
        {
            // An injected handler is owned by the caller (reused across requests);
            // the default handler belongs to the short-lived client.
            var http = _handler == null
                ? new HttpClient()
                : new HttpClient(_handler, disposeHandler: false);
            http.Timeout = timeout;
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("AIO-Hybrid-Clipboard", CurrentVersion.ToString()));
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return http;
        }
    }
}
