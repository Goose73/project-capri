namespace Capri.Automation.Apps;

/// <summary>
/// Validates that an application executable is safe and present on disk.
/// </summary>
public interface IAppPathValidator
{
    /// <summary>
    /// Validates the supplied executable path.
    /// </summary>
    AppPathValidationResult Validate(string? executablePath);
}

/// <summary>
/// Result of application path validation.
/// </summary>
/// <param name="IsValid">True when the path is safe and the file exists.</param>
/// <param name="NormalizedPath">Full normalized path when valid.</param>
/// <param name="Reason">Human-readable rejection reason when invalid.</param>
public sealed record AppPathValidationResult(bool IsValid, string? NormalizedPath, string? Reason);
