namespace Capri.Core.Abstractions;

/// <summary>
/// Stores provider secrets protected by the operating system.
/// </summary>
public interface ISecureCredentialStore
{
    /// <summary>
    /// Returns true when a credential exists for <paramref name="key"/>.
    /// </summary>
    Task<bool> HasAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the credential for <paramref name="key"/>, or null when absent.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces the credential for <paramref name="key"/>.
    /// </summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the credential for <paramref name="key"/> when present.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
