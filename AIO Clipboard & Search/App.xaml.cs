using AIO_Hybrid_Clipboard.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AIO_Hybrid_Clipboard
{
    public partial class App : Application
    {
        private const string MutexName      = "AIO_Hybrid_Clipboard_SingleInstance";
        private const string ShowSignalName = "AIO_Hybrid_Clipboard_ShowSignal";

        private static Mutex?    _mutex;
        private static bool      _ownsMutex;
        private EventWaitHandle? _showSignal;
        private MainWindow?      _mainWindow;
        private volatile bool    _isExiting;

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

            bool startMinimizedArg = e.Args.Any(a =>
                string.Equals(a, StartupService.MinimizedArg, StringComparison.OrdinalIgnoreCase));

            _mainWindow = new MainWindow();
            MainWindow  = _mainWindow;

            if (startMinimizedArg || _mainWindow.ShouldStartMinimized)
                _mainWindow.InitializeHidden();
            else
                _mainWindow.ShowLauncher();

            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            var listener = new Thread(() =>
            {
                try
                {
                    while (!_isExiting && _showSignal.WaitOne())
                    {
                        if (_isExiting) break;
                        Dispatcher.BeginInvoke(() => _mainWindow?.ShowLauncher());
                    }
                }
                catch (Exception)
                {
                    // Handle is disposed during shutdown; the listener ends cleanly.
                }
            })
            { IsBackground = true, Name = "SecondInstanceListener" };
            listener.Start();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            // Flush session & release Win32 handles deterministically on Windows logoff/shutdown.
            _isExiting = true;
            _mainWindow?.ShutdownCleanup();
            base.OnSessionEnding(e);
        }

        // Last-resort logging: without these hooks an unexpected exception kills
        // the tray app silently and leaves no trace in AIO_Cache/app.log.
        private void RegisterGlobalExceptionLogging()
        {
            DispatcherUnhandledException += (_, args) =>
            {
                Log.Error("Unhandled UI exception", args.Exception);
                args.Handled = true;
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
            _isExiting = true;
            _mainWindow?.ShutdownCleanup();

            // Wake and release the wait handle deterministically.
            try { _showSignal?.Set(); }
            catch (ObjectDisposedException) { }
            _showSignal?.Dispose();
            _showSignal = null;

            // Only the owning instance may release; a second instance never
            // acquired the mutex and releasing it would throw on shutdown.
            if (_ownsMutex)
            {
                try { _mutex?.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            _mutex?.Dispose();
            _mutex = null;

            base.OnExit(e);
        }
    }
}
