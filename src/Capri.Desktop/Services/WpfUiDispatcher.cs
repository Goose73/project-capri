using System.Windows;
using System.Windows.Threading;

namespace Capri.Desktop.Services;

/// <summary>
/// WPF dispatcher implementation.
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private static Dispatcher CurrentDispatcher => Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public bool CheckAccess() => CurrentDispatcher.CheckAccess();

    /// <inheritdoc />
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = CurrentDispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    /// <inheritdoc />
    public async Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = CurrentDispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        var dispatcher = CurrentDispatcher;
        if (dispatcher.CheckAccess())
            return func();
        return await dispatcher.InvokeAsync(func, DispatcherPriority.Normal, cancellationToken);
    }
}
