using System.Windows.Media.Imaging;

namespace AIO_Hybrid_Clipboard.Models
{
    public class ScreenshotModel
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public BitmapSource? Image { get; set; }
        public string OcrText { get; set; } = string.Empty;
    }
}
