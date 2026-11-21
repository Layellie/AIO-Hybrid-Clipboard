using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIO_Hybrid_Clipboard.Models
{
    /// <summary>
    /// A single text entry in clipboard history with smart content classification,
    /// timestamping, and pin state.
    /// </summary>
    public sealed class ClipItem : INotifyPropertyChanged
    {
        public ClipItem(string text, DateTime? createdAt = null, string? sourceApp = null, string? alias = null)
        {
            Text      = text ?? string.Empty;
            CreatedAt = createdAt ?? DateTime.Now;
            SourceApp = sourceApp?.Trim() ?? string.Empty;
            _alias    = alias?.Trim() ?? string.Empty;

            var (kind, colorHex) = ContentClassifier.Classify(Text);
            Kind        = kind;
            ColorHex    = colorHex;
            PreviewText = ContentClassifier.BuildPreview(Text);
            TooltipText = ContentClassifier.BuildTooltipText(Text);
            LineCount   = ContentClassifier.CountLines(Text);
            CharCount   = Text.Length;
        }

        public string   Text        { get; }
        public DateTime CreatedAt   { get; }
        public string   SourceApp   { get; }
        public ClipKind Kind        { get; }
        public string?  ColorHex    { get; }
        public string   PreviewText { get; }
        public string   TooltipText { get; }
        public int      LineCount   { get; }
        public int      CharCount   { get; }
        public bool     IsLargeText => CharCount > ContentClassifier.HugeTextThresholdChars;

        public string SourceAppLabel => string.IsNullOrEmpty(SourceApp)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(SourceApp);

        public bool HasSourceApp => !string.IsNullOrEmpty(SourceAppLabel);

        private string _alias;
        public string Alias
        {
            get => _alias;
            set
            {
                string clean = value?.Trim() ?? string.Empty;
                if (_alias == clean) return;
                _alias = clean;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAlias));
            }
        }

        public bool HasAlias => !string.IsNullOrEmpty(_alias);

        public bool IsUrl   => Kind == ClipKind.Url;
        public bool IsColor => Kind == ClipKind.Color && ColorHex != null;
        public bool IsCode  => Kind == ClipKind.Code;

        public string KindBadge => Kind switch
        {
            ClipKind.Url      => "URL",
            ClipKind.Color    => ColorHex ?? "HEX",
            ClipKind.Email    => "MAIL",
            ClipKind.FilePath => "PATH",
            ClipKind.Code     => "CODE",
            _                 => LineCount > 1 ? $"{LineCount}L" : "TXT"
        };

        public string TimeLabel => CreatedAt.Date == DateTime.Today
            ? CreatedAt.ToString("HH:mm")
            : CreatedAt.ToString("MMM dd");

        public string MetaSummary => LineCount > 1
            ? $"{LineCount} lines · {CharCount:N0} chars · {TimeLabel}"
            : $"{CharCount:N0} chars · {TimeLabel}";

        private bool _isPinned;
        public bool IsPinned
        {
            get => _isPinned;
            set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(); } }
        }

        public override string ToString() => Text;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
