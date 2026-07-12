using System;
using System.Threading;
using System.Windows;

namespace AIO_Hybrid_Clipboard
{
    public partial class App : Application
    {
        private const string MutexName      = "AIO_Hybrid_Clipboard_SingleInstance";
        private const string ShowSignalName = "AIO_Hybrid_Clipboard_ShowSignal";

        private static Mutex? _mutex;
        private static bool _ownsMutex;
        private EventWaitHandle? _showSignal;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, MutexName, out _ownsMutex);
            if (!_ownsMutex)
            {
                // Ask the running instance to show itself instead of nagging with a dialog.
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                    signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException) { }
                Shutdown();
                return;
            }

            base.OnStartup(e);

            var window = new MainWindow();
            window.Show();

            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            var listener = new Thread(() =>
            {
                try
                {
                    while (_showSignal.WaitOne())
                        Dispatcher.Invoke(window.ShowLauncher);
                }
                catch (Exception)
                {
                    // Handle is disposed during shutdown; the listener simply ends.
                }
            })
            { IsBackground = true, Name = "SecondInstanceListener" };
            listener.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Only the owning instance may release; a second instance never
            // acquired the mutex and releasing it would throw on shutdown.
            if (_ownsMutex) _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _showSignal?.Dispose();
            base.OnExit(e);
        }
    }
}
