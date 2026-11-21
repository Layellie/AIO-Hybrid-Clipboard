using System.Windows;
using System.Windows.Input;

namespace AIO_Hybrid_Clipboard.Views
{
    public partial class TextPromptDialog : Window
    {
        private readonly bool _multiline;

        public string ResultText => TxtInput.Text;

        public TextPromptDialog(
            string title,
            string prompt,
            string initialValue,
            bool multiline,
            string okLabel,
            string cancelLabel)
        {
            InitializeComponent();
            _multiline = multiline;

            Title          = title;
            TxtTitle.Text  = title;
            TxtPrompt.Text = prompt;
            TxtInput.Text  = initialValue ?? string.Empty;
            BtnOk.Content     = okLabel;
            BtnCancel.Content = cancelLabel;

            if (multiline)
            {
                TxtInput.AcceptsReturn = true;
                TxtInput.TextWrapping  = TextWrapping.Wrap;
                TxtInput.Height        = 170;
                Width                  = 520;
            }
            else
            {
                TxtInput.AcceptsReturn = false;
                TxtInput.TextWrapping  = TextWrapping.NoWrap;
            }

            Loaded += (_, _) =>
            {
                TxtInput.Focus();
                TxtInput.SelectAll();
            };
        }

        private void TxtInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled    = true;
            }
            else if (e.Key == Key.Enter && (!_multiline || (Keyboard.Modifiers & ModifierKeys.Control) != 0))
            {
                DialogResult = true;
                e.Handled    = true;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
