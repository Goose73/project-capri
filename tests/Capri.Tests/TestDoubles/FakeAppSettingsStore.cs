using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Security;

namespace Capri.Tests.TestDoubles;

/// <summary>
/// In-memory settings store used by provider and permission tests.
/// </summary>
public sealed class FakeAppSettingsStore : IAppSettingsStore
{
    private AppSettings _current;

    public FakeAppSettingsStore(AppSettings? initial = null)
    {
        _current = initial ?? CapriDefaultSettings.Create();
    }

    public AppSettings Current => _current;

    public event EventHandler<AppSettingsChangedEventArgs>? Changed;

    public Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var previous = _current;
        _current = settings;
        Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, settings));
        return Task.CompletedTask;
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
