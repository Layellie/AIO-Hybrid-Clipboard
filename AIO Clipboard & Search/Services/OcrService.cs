using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AIO_Hybrid_Clipboard.Services
{
    internal static class OcrService
    {
        [DllImport("AIO_SearchEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern void ProcessImageOCR(byte[] pixelData, int width, int height, int stride, StringBuilder outText, int maxLen);

        public static Task<string> RecognizeAsync(byte[] pixels, int width, int height, int stride)
        {
            return Task.Run(() =>
            {
                var sb = new StringBuilder(8192);
                ProcessImageOCR(pixels, width, height, stride, sb, sb.Capacity);
                return sb.ToString().Trim();
            });
        }
    }
}
