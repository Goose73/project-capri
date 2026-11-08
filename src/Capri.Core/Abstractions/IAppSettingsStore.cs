using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Provides access to persisted application settings.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>
    /// Most recently loaded settings.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// Raised after <see cref="Current"/> is replaced.
    /// </summary>
    event EventHandler<AppSettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Persists and applies a new settings snapshot.
    /// </summary>
    Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads settings from the backing store.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
