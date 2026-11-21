using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using AIO_Hybrid_Clipboard.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    public class TextTransformerTests
    {
        [Fact]
        public void FormatAndMinifyJson_WorkOnValidJsonAndRejectInvalid()
        {
            string? pretty = TextTransformer.Apply("{\"a\":1,\"b\":[2,3]}", TextTransformKind.FormatJson);
            Assert.NotNull(pretty);
            Assert.Contains("\n", pretty!);
            Assert.Contains("\"a\": 1", pretty!);

            string? compact = TextTransformer.Apply(pretty!, TextTransformKind.MinifyJson);
            Assert.Equal("{\"a\":1,\"b\":[2,3]}", compact);

            Assert.Null(TextTransformer.Apply("not json", TextTransformKind.FormatJson));
            Assert.Null(TextTransformer.Apply("{broken", TextTransformKind.MinifyJson));
        }

        [Fact]
        public void TextTransforms_SingleLineCaseTrimAndUrlWork()
        {
            Assert.Equal("hello world foo", TextTransformer.Apply("  hello\r\n  world\t\tfoo  ", TextTransformKind.SingleLine));
            Assert.Equal("HELLO", TextTransformer.Apply("hello", TextTransformKind.UpperCase));
            Assert.Equal("hello", TextTransformer.Apply("HELLO", TextTransformKind.LowerCase));
            Assert.Equal($"a{Environment.NewLine}b", TextTransformer.Apply("  a  \r\n  b  ", TextTransformKind.TrimLines));
            Assert.Equal("hello world/test", TextTransformer.Apply("hello%20world%2Ftest", TextTransformKind.UrlDecode));
            Assert.Equal("hello%20world", TextTransformer.Apply("hello world", TextTransformKind.UrlEncode));
        }
    }

    public class AppExclusionAndTtlTests
    {
        [Fact]
        public void ParseExcludedApps_NormalizesWithAndWithoutExe()
        {
            var set = ClipboardPrivacyGuard.ParseExcludedApps("KeePassXC.exe, mstsc; \"1Password.exe\"\nBitwarden");

            Assert.True(ClipboardPrivacyGuard.IsProcessExcluded("keepassxc.exe", set));
            Assert.True(ClipboardPrivacyGuard.IsProcessExcluded("mstsc.exe", set));
            Assert.True(ClipboardPrivacyGuard.IsProcessExcluded("1Password.exe", set));
            Assert.True(ClipboardPrivacyGuard.IsProcessExcluded("Bitwarden.exe", set));
            Assert.False(ClipboardPrivacyGuard.IsProcessExcluded("Code.exe", set));
        }

        [Fact]
        public void PurgeExpired_RemovesOldUnpinnedOnlyAndPreservesPinned()
        {
            var history = new HistoryService();
            var now = new DateTime(2026, 11, 21, 12, 0, 0);

            var oldPinned   = new ClipItem("old-pin",   now.AddDays(-20)) { IsPinned = true };
            var oldUnpinned = new ClipItem("old-temp",  now.AddDays(-20)) { IsPinned = false };
            var freshText   = new ClipItem("fresh",     now.AddDays(-2))  { IsPinned = false };
            history.Texts.Add(oldPinned);
            history.Texts.Add(oldUnpinned);
            history.Texts.Add(freshText);

            var oldPinShot  = new ScreenshotModel { Name = "old-pin-s",  Path = "p1", CapturedAt = now.AddDays(-20), IsPinned = true };
            var oldTempShot = new ScreenshotModel { Name = "old-temp-s", Path = "p2", CapturedAt = now.AddDays(-20), IsPinned = false };
            var freshShot   = new ScreenshotModel { Name = "fresh-s",    Path = "p3", CapturedAt = now.AddDays(-1),  IsPinned = false };
            history.Screenshots.Add(oldPinShot);
            history.Screenshots.Add(oldTempShot);
            history.Screenshots.Add(freshShot);

            var removed = history.PurgeExpired(retentionDays: 7, now: now);

            Assert.Equal(2, history.Texts.Count);
            Assert.Contains(oldPinned, history.Texts);
            Assert.Contains(freshText, history.Texts);
            Assert.DoesNotContain(oldUnpinned, history.Texts);

            Assert.Single(removed);
            Assert.Same(oldTempShot, removed[0]);
            Assert.Equal(2, history.Screenshots.Count);
        }

        [Fact]
        public void HistorySearch_MatchesAliasAndSourceApp_AndDuplicatePreservesAlias()
        {
            var history = new HistoryService();
            history.InsertText("SELECT * FROM users;", sourceApp: "ssms.exe", alias: "prod-users");

            // Re-copying same SQL preserves existing alias!
            history.InsertText("SELECT * FROM users;");
            Assert.Equal("prod-users", history.Texts[0].Alias);
            Assert.Equal("ssms.exe", history.Texts[0].SourceApp);

            Assert.Single(history.FilterTexts("prod-users"));
            Assert.Single(history.FilterTexts("ssms"));
        }
    }

    public class DpapiAndChecksumTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly IDisposable _scope;

        public DpapiAndChecksumTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "aio-dpapi-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _scope = AppPaths.OverrideCacheFolder(_tempDir);
        }

        public void Dispose()
        {
            _scope.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { }
        }

        [Fact]
        public void SessionCrypto_ProtectThenUnprotect_RoundtripsAndRejectsTamperedData()
        {
            byte[] plain = Encoding.UTF8.GetBytes("{\"secret\":\"top-secret-history\"}");
            byte[] encrypted = SessionCrypto.Protect(plain);

            Assert.True(SessionCrypto.IsEncrypted(encrypted));
            Assert.NotEqual(plain, encrypted);
            Assert.True(SessionCrypto.TryUnprotect(encrypted, out byte[] decrypted));
            Assert.Equal(plain, decrypted);

            // Flip a byte in the ciphertext — DPAPI integrity check must reject it.
            encrypted[^1] ^= 0xFF;
            Assert.False(SessionCrypto.TryUnprotect(encrypted, out _));
        }

        [Fact]
        public void SessionStore_EncryptedSaveAndLoad_RoundtripsAliasAndSourceApp()
        {
            var items = new[]
            {
                new ClipItem("SELECT 1", sourceApp: "Code.exe", alias: "health-check") { IsPinned = true }
            };

            SessionStore.Save(items, Array.Empty<ScreenshotModel>(), encrypt: true);

            byte[] diskBytes = File.ReadAllBytes(AppPaths.SessionFile);
            Assert.True(SessionCrypto.IsEncrypted(diskBytes));
            Assert.DoesNotContain("SELECT 1", Encoding.UTF8.GetString(diskBytes));

            var (loaded, _) = SessionStore.Load();
            Assert.Single(loaded);
            Assert.Equal("SELECT 1", loaded[0].Text);
            Assert.Equal("Code.exe", loaded[0].SourceApp);
            Assert.Equal("health-check", loaded[0].Alias);
            Assert.True(loaded[0].IsPinned);
        }

        [Fact]
        public void UpdateService_ParseExpectedSha256_HandlesStandardFormats()
        {
            string hash = new string('A', 64);
            string body = $"{hash}  AIO_Hybrid_Clipboard_Setup_v2.0.0.exe\r\n" +
                          $"{new string('b', 64)} *other.exe\r\n";

            Assert.Equal(hash, UpdateService.ParseExpectedSha256(body, "AIO_Hybrid_Clipboard_Setup_v2.0.0.exe"));
            Assert.Equal(new string('B', 64), UpdateService.ParseExpectedSha256(body, "other.exe"));
            Assert.Null(UpdateService.ParseExpectedSha256(body, "missing.exe"));
        }

        [Fact]
        public async Task UpdateService_DownloadInstaller_VerifiesSha256AndRejectsMismatch()
        {
            byte[] payload = Encoding.UTF8.GetBytes("fake-installer-binary-payload-v2");
            string goodHash = Convert.ToHexString(SHA256.HashData(payload));
            string badHash  = new string('0', 64);

            string fileName = $"AIO_Verify_Setup_{Guid.NewGuid():N}.exe";
            string exeUrl   = $"https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v2.0.0/{fileName}";
            string sumUrl   = "https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v2.0.0/SHA256SUMS.txt";

            var goodHandler = new RouteHttpHandler(new Dictionary<string, byte[]>
            {
                [exeUrl] = payload,
                [sumUrl] = Encoding.ASCII.GetBytes($"{goodHash}  {fileName}\n")
            });

            string? downloaded = null;
            try
            {
                downloaded = await new UpdateService(goodHandler).DownloadInstallerAsync(exeUrl, checksumsUrl: sumUrl);
                Assert.True(File.Exists(downloaded));
            }
            finally
            {
                if (downloaded != null && File.Exists(downloaded)) File.Delete(downloaded);
            }

            var badHandler = new RouteHttpHandler(new Dictionary<string, byte[]>
            {
                [exeUrl] = payload,
                [sumUrl] = Encoding.ASCII.GetBytes($"{badHash}  {fileName}\n")
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new UpdateService(badHandler).DownloadInstallerAsync(exeUrl, checksumsUrl: sumUrl));

            string expectedPath = Path.Combine(Path.GetTempPath(), fileName);
            Assert.False(File.Exists(expectedPath));
            Assert.False(File.Exists(expectedPath + ".part"));
        }

        [Fact]
        public void MainViewModel_TransformAliasEditOcrAndSavePngWork()
        {
            var dialogs = new FakeDialogService { PromptAnswer = "my-json" };
            var settings = new SettingsService(@"SOFTWARE\AIO_ClipboardSearch_PFTests_" + Guid.NewGuid().ToString("N"));
            var vm = new MainViewModel(null, dialogs, settings, loadSessionSync: true)
            {
                AutoSaveEnabled  = false,
                SearchDebounceMs = 0,
                HideOnCopy       = false
            };

            vm.History.Texts.Clear();
            vm.History.InsertText("{\"k\":1}");
            var clip = vm.History.Texts[0];

            // Alias
            vm.EditAlias(clip);
            Assert.Equal("my-json", clip.Alias);

            // JSON format transform
            vm.TransformClip(clip, TextTransformKind.FormatJson);
            Assert.Contains("\"k\": 1", vm.History.Texts[0].Text);

            // Edit OCR text
            string srcPng = Path.Combine(_tempDir, "src.png");
            string dstPng = Path.Combine(_tempDir, "saved.png");
            File.WriteAllBytes(srcPng, new byte[] { 137, 80, 78, 71 });

            var shot = new ScreenshotModel { Name = "s", Path = srcPng, OcrText = "old" };
            dialogs.PromptAnswer   = "corrected ocr text";
            dialogs.SavePathAnswer = dstPng;

            vm.EditOcrText(shot);
            Assert.Equal("corrected ocr text", shot.OcrText);
            Assert.Equal("corrected ocr text", vm.History.Texts[0].Text);

            vm.SaveScreenshotAs(shot);
            Assert.True(File.Exists(dstPng));
        }

        private sealed class RouteHttpHandler(Dictionary<string, byte[]> routes) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
                if (routes.TryGetValue(url, out var bytes))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
