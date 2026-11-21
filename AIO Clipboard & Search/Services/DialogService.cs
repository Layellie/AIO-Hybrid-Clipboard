using AIO_Hybrid_Clipboard.Views;
using Microsoft.Win32;
using System.Windows;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Abstraction over user dialogs so view models stay testable —
    /// tests inject a fake instead of popping real message boxes or file pickers.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Yes/No question. Returns true when the user confirms.</summary>
        bool Confirm(string message, string title);

        void ShowError(string message, string title);

        /// <summary>Shows a single-line or multi-line input dialog. Returns null when cancelled.</summary>
        string? PromptText(string title, string prompt, string initialValue, bool multiline = false, string? okLabel = null, string? cancelLabel = null);

        /// <summary>Shows a Save As PNG dialog and returns the selected path, or null when cancelled.</summary>
        string? PickSavePngPath(string suggestedFileName);
    }

    internal sealed class MessageBoxDialogService : IDialogService
    {
        public bool Confirm(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes;

        public void ShowError(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public string? PromptText(
            string title,
            string prompt,
            string initialValue,
            bool multiline = false,
            string? okLabel = null,
            string? cancelLabel = null)
        {
            var dlg = new TextPromptDialog(
                title,
                prompt,
                initialValue,
                multiline,
                okLabel     ?? "Save",
                cancelLabel ?? "Cancel");

            if (Application.Current?.MainWindow is { IsVisible: true } owner)
                dlg.Owner = owner;
            else
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            return dlg.ShowDialog() == true ? dlg.ResultText : null;
        }

        public string? PickSavePngPath(string suggestedFileName)
        {
            var dlg = new SaveFileDialog
            {
                Filter      = "PNG Image (*.png)|*.png",
                DefaultExt  = ".png",
                FileName    = suggestedFileName,
                AddExtension = true
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}
