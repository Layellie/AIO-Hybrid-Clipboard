using System.Windows;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Abstraction over user dialogs so view models stay testable —
    /// tests inject a fake instead of popping real message boxes.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Yes/No question. Returns true when the user confirms.</summary>
        bool Confirm(string message, string title);

        void ShowError(string message, string title);
    }

    internal sealed class MessageBoxDialogService : IDialogService
    {
        public bool Confirm(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes;

        public void ShowError(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
