using AIO_Hybrid_Clipboard.Services;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    /// <summary>
    /// End-to-end tests against the real native OCR engine. They require
    /// AIO_SearchEngine.dll (built Release|x64) next to the test binaries and
    /// the Windows OCR feature, both of which are present on CI and dev boxes.
    /// </summary>
    public class OcrIntegrationTests
    {
        private static bool EngineAvailable =>
            File.Exists(Path.Combine(AppContext.BaseDirectory, "AIO_SearchEngine.dll"));

        private static Bitmap RenderLinesBitmap(string[] lines, int width = 700, int height = 240)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using var font = new Font("Arial", 32, FontStyle.Bold);
                for (int i = 0; i < lines.Length; i++)
                    g.DrawString(lines[i], font, Brushes.Black, 24, 24 + i * 80);
            }
            return bitmap;
        }

        private static (byte[] pixels, int width, int height, int stride) LockPixels(Bitmap bitmap)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                       ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int stride = data.Stride;
                var pixels = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return (pixels, bitmap.Width, bitmap.Height, stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        [Fact]
        public void IsEngineAvailable_DoesNotThrowWhenProbed()
        {
            if (!EngineAvailable) return;

            bool available = OcrService.IsEngineAvailable();
            // On machines with OCR language packs installed, this returns true; on stripped server images, false.
            Assert.IsType<bool>(available);
        }

        [Fact]
        public async Task RecognizeAsync_ReadsRenderedText()
        {
            if (!EngineAvailable) return;

            using var bmp = RenderLinesBitmap(new[] { "HELLO WORLD 123" }, 600, 140);
            var (pixels, width, height, stride) = LockPixels(bmp);

            var (status, result) = await OcrService.RecognizeWithStatusAsync(pixels, width, height, stride);
            if (status == OcrService.OcrStatus.EngineUnavailable) return;

            Assert.Equal(OcrService.OcrStatus.Success, status);
            Assert.Contains("HELLO", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("123",   result);
        }

        [Fact]
        public async Task RecognizeAsync_PreservesLineBreaksOnMultiLineText()
        {
            if (!EngineAvailable) return;

            using var bmp = RenderLinesBitmap(new[] { "ALPHA ONE 100", "BETA TWO 200" }, 700, 240);
            var (pixels, width, height, stride) = LockPixels(bmp);

            var (status, result) = await OcrService.RecognizeWithStatusAsync(pixels, width, height, stride);
            if (status == OcrService.OcrStatus.EngineUnavailable) return;

            Assert.Equal(OcrService.OcrStatus.Success, status);
            Assert.Contains("ALPHA", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("BETA",  result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\n",    result);
        }

        [Fact]
        public async Task RecognizeImageFileAsync_ReadsFullResolutionPngFromDisk()
        {
            if (!EngineAvailable) return;

            string tempPng = Path.Combine(Path.GetTempPath(), $"aio-ocr-test-{Guid.NewGuid():N}.png");
            try
            {
                using (var bmp = RenderLinesBitmap(new[] { "SAVED PHOTO 987" }, 600, 140))
                    bmp.Save(tempPng, ImageFormat.Png);

                var (status, result) = await OcrService.RecognizeImageFileAsync(tempPng);
                if (status == OcrService.OcrStatus.EngineUnavailable) return;

                Assert.Equal(OcrService.OcrStatus.Success, status);
                Assert.Contains("PHOTO", result, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("987",   result);
            }
            finally
            {
                if (File.Exists(tempPng)) File.Delete(tempPng);
            }
        }

        [Fact]
        public async Task RecognizeAsync_ReturnsEmptyForBlankImage()
        {
            if (!EngineAvailable) return;

            const int width = 200, height = 100, stride = width * 4;
            var blank = new byte[height * stride];
            Array.Fill(blank, (byte)0xFF); // solid white

            var (status, result) = await OcrService.RecognizeWithStatusAsync(blank, width, height, stride);
            if (status == OcrService.OcrStatus.EngineUnavailable) return;

            Assert.Equal(OcrService.OcrStatus.NoText, status);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void GetAvailableLanguages_IncludesAutoAndInstalledPacks()
        {
            if (!EngineAvailable) return;

            var langs = OcrService.GetAvailableLanguages();
            Assert.NotEmpty(langs);
            Assert.Equal(OcrService.AutoLanguageTag, langs[0]);
        }

        [Fact]
        public async Task RecognizeAsync_WithExplicitEnUsLanguage_ReadsEnglishITextAccurately()
        {
            if (!EngineAvailable) return;

            var langs = OcrService.GetAvailableLanguages();
            if (!langs.Contains("en-US", StringComparer.OrdinalIgnoreCase)) return;

            using var bmp = RenderLinesBitmap(new[] { "FIRST LINE 100", "DISK FILE 987" }, 700, 240);
            var (pixels, width, height, stride) = LockPixels(bmp);

            var (status, result) = await OcrService.RecognizeWithStatusAsync(
                pixels, width, height, stride, languageTag: "en-US");
            if (status == OcrService.OcrStatus.EngineUnavailable) return;

            Assert.Equal(OcrService.OcrStatus.Success, status);
            Assert.Contains("FIRST", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DISK",  result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RecognizeAsync_RejectsInvalidArguments()
        {
            if (!EngineAvailable) return;

            var (status, _) = await OcrService.RecognizeWithStatusAsync(new byte[4], -1, -1, 0);

            Assert.Equal(OcrService.OcrStatus.InvalidArgument, status);
        }
    }
}
