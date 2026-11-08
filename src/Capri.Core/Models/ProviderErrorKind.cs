namespace Capri.Core.Models;

/// <summary>
/// Broad category of a provider failure.
/// </summary>
public enum ProviderErrorKind
{
    Unknown = 0,
    Configuration,
    Network,
    Authentication,
    Authorization,
    RateLimited,
    InvalidRequest,
    NotFound,
    Timeout,
    ServerError,
    Cancelled
}
