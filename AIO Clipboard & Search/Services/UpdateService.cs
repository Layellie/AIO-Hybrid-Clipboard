using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Checks GitHub Releases for a newer version and safely downloads, SHA-256 verifies,
    /// and launches the installer. Uses a shared static <see cref="HttpClient"/> in production
    /// and supports an injectable <see cref="HttpMessageHandler"/> for offline unit testing.
    /// </summary>
    internal class UpdateService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/Layellie/AIO-Hybrid-Clipboard/releases/latest";
        private const string ReleasesPage     = "https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/latest";

        private static readonly HttpClient SharedClient = CreateConfiguredClient(
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) },
            disposeHandler: true);

        private readonly HttpClient _client;

        public UpdateService(HttpMessageHandler? handler = null)
        {
            _client = handler == null
                ? SharedClient
                : CreateConfiguredClient(handler, disposeHandler: false);
        }

        internal sealed record UpdateInfo(Version LatestVersion, string? InstallerUrl, string? ChecksumsUrl = null);

        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return Normalize(v);
            }
        }

        internal static Version Normalize(Version v) =>
            new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

        /// <summary>
        /// Verifies that an installer download URL uses HTTPS and points strictly to a trusted
        /// GitHub release host without path traversal segments, ending with <c>.exe</c>.
        /// </summary>
        internal static bool IsTrustedDownloadUrl(string? url) =>
            IsTrustedGitHubAssetUrl(url, requiredExtension: ".exe");

        internal static bool IsTrustedChecksumUrl(string? url) =>
            IsTrustedGitHubAssetUrl(url, requiredExtension: ".txt");

        private static bool IsTrustedGitHubAssetUrl(string? url, string requiredExtension)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            string host = uri.Host;
            bool trustedHost =
                string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

            if (!trustedHost)
                return false;

            if (url.Contains("..", StringComparison.Ordinal) || uri.AbsolutePath.Contains("..", StringComparison.Ordinal))
                return false;

            return TryGetSafeFileName(uri, requiredExtension, out _);
        }

        internal static bool TryGetSafeFileName(Uri uri, string requiredExtension, out string safeFileName)
        {
            safeFileName = string.Empty;

            string rawName = Uri.UnescapeDataString(Path.GetFileName(uri.LocalPath));
            if (string.IsNullOrWhiteSpace(rawName))
                return false;

            if (rawName.Contains("..", StringComparison.Ordinal) ||
                rawName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                rawName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            if (!rawName.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
                return false;

            safeFileName = rawName;
            return true;
        }

        /// <summary>
        /// Parses standard <c>SHA256SUMS.txt</c> lines (<c>&lt;64-hex&gt;  &lt;filename&gt;</c>)
        /// and returns the normalized 64-char uppercase hex digest for <paramref name="fileName"/>.
        /// </summary>
        internal static string? ParseExpectedSha256(string checksumFileContent, string fileName)
        {
            if (string.IsNullOrWhiteSpace(checksumFileContent) || string.IsNullOrWhiteSpace(fileName))
                return null;

            string[] lines = checksumFileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length < 66) continue;

                int sep = line.IndexOfAny(new[] { ' ', '\t' });
                if (sep != 64) continue;

                string hashPart = line[..64];
                if (!IsValidHex64(hashPart)) continue;

                string namePart = line[(sep + 1)..].Trim().TrimStart('*');
                if (string.Equals(Path.GetFileName(namePart), fileName, StringComparison.OrdinalIgnoreCase))
                    return hashPart.ToUpperInvariant();
            }

            return null;
        }

        internal static string ComputeFileSha256(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] hash = SHA256.HashData(fs);
            return Convert.ToHexString(hash);
        }

        internal static bool VerifyFileSha256(string filePath, string expectedHex)
        {
            if (string.IsNullOrWhiteSpace(expectedHex) || !IsValidHex64(expectedHex) || !File.Exists(filePath))
                return false;

            byte[] actualBytes   = Convert.FromHexString(ComputeFileSha256(filePath));
            byte[] expectedBytes = Convert.FromHexString(expectedHex);
            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }

        private static bool IsValidHex64(string s)
        {
            if (s.Length != 64) return false;
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        /// <summary>Returns the latest release info, or null when it cannot be determined.</summary>
        public async Task<UpdateInfo?> CheckLatestAsync(CancellationToken ct = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await _client.GetAsync(LatestReleaseApi, timeoutCts.Token);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeoutCts.Token));
            var root = doc.RootElement;

            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return null;

            string? installerUrl = null;
            string? checksumsUrl = null;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(name) || !asset.TryGetProperty("browser_download_url", out var urlProp))
                        continue;

                    string? candidate = urlProp.GetString();

                    if (installerUrl == null &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsTrustedDownloadUrl(candidate))
                            installerUrl = candidate;
                        else
                            Log.Warn($"Rejected untrusted installer download URL from release metadata: {candidate}");
                    }
                    else if (checksumsUrl == null &&
                             string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) &&
                             IsTrustedChecksumUrl(candidate))
                    {
                        checksumsUrl = candidate;
                    }
                }
            }

            return new UpdateInfo(Normalize(latest), installerUrl, checksumsUrl);
        }

        /// <summary>
        /// Downloads a validated GitHub installer into %TEMP% with progress reporting,
        /// optional SHA-256 verification against <c>SHA256SUMS.txt</c>, and atomic rename (.part -> .exe).
        /// </summary>
        public async Task<string> DownloadInstallerAsync(
            string url,
            IProgress<double>? progress = null,
            CancellationToken ct = default,
            string? checksumsUrl = null)
        {
            if (!IsTrustedDownloadUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !TryGetSafeFileName(uri, ".exe", out string fileName))
            {
                throw new InvalidOperationException("Installer URL failed security validation.");
            }

            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (!tempRoot.EndsWith(Path.DirectorySeparatorChar))
                tempRoot += Path.DirectorySeparatorChar;

            string target = Path.GetFullPath(Path.Combine(tempRoot, fileName));
            if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Installer target path escaped temporary directory.");

            string partPath = target + ".part";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

            using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            try
            {
                await using (var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token))
                await using (var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    byte[] buffer = new byte[81920];
                    long copied = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, timeoutCts.Token)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token);
                        copied += read;

                        if (totalBytes is > 0 && progress != null)
                            progress.Report(Math.Min(100.0, copied * 100.0 / totalBytes.Value));
                    }
                }

                // Verify SHA-256 integrity before promoting .part -> .exe when checksums asset is published.
                if (!string.IsNullOrWhiteSpace(checksumsUrl))
                {
                    if (!IsTrustedChecksumUrl(checksumsUrl))
                        throw new InvalidOperationException("Checksum asset URL failed security validation.");

                    string checksumBody = await _client.GetStringAsync(checksumsUrl, timeoutCts.Token);
                    string? expectedHash = ParseExpectedSha256(checksumBody, fileName);
                    if (expectedHash == null || !VerifyFileSha256(partPath, expectedHash))
                        throw new InvalidOperationException("Downloaded installer SHA-256 verification failed.");
                }

                File.Move(partPath, target, overwrite: true);
                progress?.Report(100.0);
                return target;
            }
            catch
            {
                try { if (File.Exists(partPath)) File.Delete(partPath); }
                catch { /* best-effort cleanup */ }
                throw;
            }
        }

        public void RunInstaller(string path) =>
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        public void OpenReleasesPage() =>
            Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });

        private static HttpClient CreateConfiguredClient(HttpMessageHandler handler, bool disposeHandler)
        {
            var http = new HttpClient(handler, disposeHandler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("AIO-Hybrid-Clipboard", CurrentVersion.ToString()));
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return http;
        }
    }
}
