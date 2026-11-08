using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Abstraction over a chat completion backend.
/// </summary>
public interface IChatProvider
{
    /// <summary>
    /// Stable identifier for the provider implementation.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Generates a complete response in one operation.
    /// </summary>
    Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams incremental response chunks.
    /// </summary>
    IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Probes provider availability.
    /// </summary>
    Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}
