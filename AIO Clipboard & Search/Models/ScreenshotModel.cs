using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AIO_Hybrid_Clipboard.Models
{
    public class ScreenshotModel : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public BitmapSource? Image { get; set; }

        // OCR completes on a background task after the model is already displayed,
        // so this property must raise change notifications.
        private string _ocrText = string.Empty;
        public string OcrText
        {
            get => _ocrText;
            set { if (_ocrText != value) { _ocrText = value; OnPropertyChanged(); } }
        }

        private bool _isPinned;
        public bool IsPinned
        {
            get => _isPinned;
            set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
