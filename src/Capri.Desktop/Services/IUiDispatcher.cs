namespace Capri.Desktop.Services;

/// <summary>
/// Marshals work to the UI thread. Abstracted for testability and to centralize dispatcher usage.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Returns true when the caller is already on the UI thread.</summary>
    bool CheckAccess();

    /// <summary>Invokes <paramref name="action"/> on the UI thread.</summary>
    void Invoke(Action action);

    /// <summary>Invokes <paramref name="action"/> asynchronously on the UI thread.</summary>
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}
