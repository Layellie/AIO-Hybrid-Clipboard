using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIO_Hybrid_Clipboard.Models
{
    /// <summary>
    /// A single text entry in the clipboard history. Carries a pin flag so the
    /// user can keep important entries from being evicted by the size limit.
    /// </summary>
    public sealed class ClipItem : INotifyPropertyChanged
    {
        public ClipItem(string text) => Text = text;

        public string Text { get; }

        private bool _isPinned;
        public bool IsPinned
        {
            get => _isPinned;
            set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(); } }
        }

        // Lets existing string-based code paths (Enter to copy, search) keep working.
        public override string ToString() => Text;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
