namespace Capri.Core.Models;

/// <summary>
/// Event data raised after application settings are updated or reloaded.
/// </summary>
public sealed class AppSettingsChangedEventArgs : EventArgs
{
    /// <summary>
    /// Creates event data for a settings change.
    /// </summary>
    /// <param name="previous">Settings snapshot before the change.</param>
    /// <param name="current">Settings snapshot after the change.</param>
    public AppSettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>
    /// Settings snapshot before the change.
    /// </summary>
    public AppSettings Previous { get; }

    /// <summary>
    /// Settings snapshot after the change.
    /// </summary>
    public AppSettings Current { get; }
}
