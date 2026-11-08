using Capri.Core.Abstractions;

namespace Capri.Tests.TestDoubles;

/// <summary>
/// In-memory credential store used by provider tests.
/// </summary>
public sealed class FakeCredentialStore : ISecureCredentialStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task<bool> HasAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_values.ContainsKey(key));
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _values.Remove(key);
        return Task.CompletedTask;
    }
}
