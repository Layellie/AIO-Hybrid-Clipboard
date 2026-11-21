using AIO_Hybrid_Clipboard.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AIO_Hybrid_Clipboard.Common
{
    /// <summary>
    /// Asynchronous ICommand with built-in re-entrancy protection and error logging.
    /// Avoids async-void lambdas inside RelayCommand.
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private readonly bool _allowConcurrent;
        private bool _isExecuting;

        public AsyncRelayCommand(
            Func<object?, Task> execute,
            Predicate<object?>? canExecute = null,
            bool allowConcurrent = false)
        {
            _execute         = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute      = canExecute;
            _allowConcurrent = allowConcurrent;
        }

        public bool IsExecuting => _isExecuting;

        public bool CanExecute(object? parameter) =>
            (_allowConcurrent || !_isExecuting) && (_canExecute?.Invoke(parameter) ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _execute(parameter);
            }
            catch (Exception ex)
            {
                Log.Error("AsyncRelayCommand execution failed", ex);
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
