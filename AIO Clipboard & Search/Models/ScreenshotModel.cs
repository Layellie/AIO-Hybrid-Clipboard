using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AIO_Hybrid_Clipboard.Models
{
    public class ScreenshotModel : INotifyPropertyChanged
    {
        public string   Name        { get; set; } = string.Empty;
        public string   Path        { get; set; } = string.Empty;
        public string   ContentHash { get; set; } = string.Empty;
        public string   SourceApp   { get; set; } = string.Empty;
        public int      PixelWidth  { get; set; }
        public int      PixelHeight { get; set; }
        public DateTime CapturedAt  { get; set; } = DateTime.Now;

        public string SourceAppLabel => string.IsNullOrEmpty(SourceApp)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(SourceApp);

        public bool HasSourceApp => !string.IsNullOrEmpty(SourceAppLabel);

        /// <summary>Low-memory 320px frozen thumbnail image for gallery display.</summary>
        public BitmapSource? Image { get; set; }

        private BitmapSource? _previewImage;
        /// <summary>
        /// Transient on-demand hover preview loaded from disk and released immediately
        /// when the tooltip closes or the launcher hides, keeping steady-state RAM low.
        /// </summary>
        public BitmapSource? PreviewImage
        {
            get => _previewImage;
            set
            {
                if (ReferenceEquals(_previewImage, value)) return;
                _previewImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayPreviewImage));
            }
        }

        public BitmapSource? DisplayPreviewImage => _previewImage ?? Image;

        public void ReleasePreview() => PreviewImage = null;

        public string ResolutionText => PixelWidth > 0 && PixelHeight > 0
            ? $"{PixelWidth}×{PixelHeight}"
            : string.Empty;

        public string TimeLabel => CapturedAt.Date == DateTime.Today
            ? CapturedAt.ToString("HH:mm")
            : CapturedAt.ToString("MMM dd");

        private string _ocrText = string.Empty;
        public string OcrText
        {
            get => _ocrText;
            set
            {
                if (_ocrText == value) return;
                _ocrText = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOcrText));
                OnPropertyChanged(nameof(OcrPreview));
            }
        }

        public bool HasOcrText => !string.IsNullOrWhiteSpace(_ocrText);

        public string OcrPreview => string.IsNullOrWhiteSpace(_ocrText)
            ? string.Empty
            : ContentClassifier.BuildPreview(_ocrText, 120);

        private bool _isOcrRunning;
        public bool IsOcrRunning
        {
            get => _isOcrRunning;
            set { if (_isOcrRunning != value) { _isOcrRunning = value; OnPropertyChanged(); } }
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
