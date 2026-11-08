namespace Capri.Core.Models;

/// <summary>
/// A provider-neutral description of a failed chat operation.
/// </summary>
/// <param name="Kind">Broad category of the failure.</param>
/// <param name="Message">Human-readable failure detail.</param>
/// <param name="Code">Optional provider-specific error code.</param>
/// <param name="StatusCode">Optional HTTP status code.</param>
public sealed record ProviderError(
    ProviderErrorKind Kind,
    string Message,
    string? Code = null,
    int? StatusCode = null);
