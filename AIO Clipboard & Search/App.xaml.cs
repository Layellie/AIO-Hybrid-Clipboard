using System.Threading;
using System.Windows;

namespace AIO_Hybrid_Clipboard
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private static bool _ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "AIO_Hybrid_Clipboard_SingleInstance", out _ownsMutex);
            if (!_ownsMutex)
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
            // Only the owning instance may release; a second instance never
            // acquired the mutex and releasing it would throw on shutdown.
            if (_ownsMutex) _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
