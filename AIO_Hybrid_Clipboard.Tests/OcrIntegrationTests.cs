using AIO_Hybrid_Clipboard.Services;
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
            File.Exists(Path.Combine(System.AppContext.BaseDirectory, "AIO_SearchEngine.dll"));

        private static (byte[] pixels, int width, int height, int stride) RenderText(string text)
        {
            const int width = 600, height = 160;
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using var font = new Font("Arial", 36, System.Drawing.FontStyle.Bold);
                g.DrawString(text, font, Brushes.Black, 20, 40);
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                                       ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int stride = data.Stride;
                var pixels = new byte[stride * height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return (pixels, width, height, stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        [Fact]
        public async Task RecognizeAsync_ReadsRenderedText()
        {
            if (!EngineAvailable) return; // native DLL not built on this machine

            var (pixels, width, height, stride) = RenderText("HELLO WORLD 123");

            var (status, result) = await OcrService.RecognizeWithStatusAsync(pixels, width, height, stride);
            if (status == OcrService.OcrStatus.EngineUnavailable) return; // no OCR language pack (e.g. server CI image)

            Assert.Equal(OcrService.OcrStatus.Success, status);
            Assert.Contains("HELLO", result, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("123",   result);
        }

        [Fact]
        public async Task RecognizeAsync_ReturnsEmptyForBlankImage()
        {
            if (!EngineAvailable) return;

            const int width = 200, height = 100, stride = width * 4;
            var blank = new byte[height * stride];
            for (int i = 0; i < blank.Length; i++) blank[i] = 0xFF; // solid white

            var (status, result) = await OcrService.RecognizeWithStatusAsync(blank, width, height, stride);
            if (status == OcrService.OcrStatus.EngineUnavailable) return;

            Assert.Equal(OcrService.OcrStatus.NoText, status);
            Assert.Equal(string.Empty, result);
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
