using AIO_Hybrid_Clipboard.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
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
            RegisterGlobalExceptionLogging();

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

        // Last-resort logging: without these hooks an unexpected exception kills
        // the tray app silently and leaves no trace in AIO_Cache/app.log.
        private void RegisterGlobalExceptionLogging()
        {
            DispatcherUnhandledException += (_, args) =>
            {
                Log.Error("Unhandled UI exception", args.Exception);
                args.Handled = true; // keep the tray app alive; state-corrupting bugs still surface in the log
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log.Error("Unhandled domain exception (terminating)", args.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Log.Error("Unobserved task exception", args.Exception);
                args.SetObserved();
            };
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
