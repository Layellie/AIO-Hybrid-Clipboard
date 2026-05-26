using System.Threading;
using System.Windows;

namespace AIO_Hybrid_Clipboard
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "AIO_Hybrid_Clipboard_SingleInstance", out bool isNewInstance);
            if (!isNewInstance)
            {
                MessageBox.Show(
                    "AIO Hybrid Clipboard is already running.",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }
            base.OnStartup(e);
            new MainWindow().Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
