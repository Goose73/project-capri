using System.Windows;
using System.Windows.Threading;

namespace Capri.Tests.Autonomy;

/// <summary>
/// Hosts the single WPF <see cref="Application"/> for STA UI integration tests.
/// WPF permits only one Application per process, so every UI test shares this dispatcher.
/// The dispatcher thread is a background STA thread and never touches OS application processes.
/// </summary>
internal static class WpfTestHost
{
    private static readonly Lazy<Host> Shared = new(static () => new Host(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static Task<T> InvokeAsync<T>(Func<Task<T>> action) => Shared.Value.InvokeAsync(action);

    public static Task InvokeAsync(Func<Task> action) => Shared.Value.InvokeAsync(action);

    private sealed class Host
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;
        private Dispatcher _dispatcher = null!;
        private Exception? _startupFailure;

        public Host()
        {
            _thread = new Thread(RunDispatcherThread)
            {
                IsBackground = true,
                Name = "Capri.Tests.WpfDispatcher"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            try
            {
                _ready.Task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("WPF test host failed to start.", ex);
            }

            if (_startupFailure is not null)
            {
                throw new InvalidOperationException("WPF test host failed to start.", _startupFailure);
            }
        }

        public Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (_dispatcher.CheckAccess())
            {
                return action();
            }

            return _dispatcher.InvokeAsync(action).Task.Unwrap();
        }

        public Task InvokeAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (_dispatcher.CheckAccess())
            {
                return action();
            }

            return _dispatcher.InvokeAsync(action).Task.Unwrap();
        }

        private void RunDispatcherThread()
        {
            try
            {
                // Creating Application establishes Application.Current and its Dispatcher.
                // Explicit shutdown keeps the host alive when an approval window closes.
#pragma warning disable CA1806 // Application is deliberately retained through Application.Current.
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
#pragma warning restore CA1806
                _dispatcher = Dispatcher.CurrentDispatcher;
                _ready.TrySetResult();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _startupFailure = ex;
                _ready.TrySetException(ex);
            }
        }
    }
}
