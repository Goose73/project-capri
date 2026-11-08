namespace Capri.Automation.Apps;

/// <summary>
/// Default <see cref="IAppPathValidator"/>. Path structure is checked by
/// <see cref="AppPathPolicy"/> and existence by <see cref="File.Exists(string)"/>.
/// </summary>
public sealed class AppPathValidator : IAppPathValidator
{
    private readonly Func<string, bool> _fileExists;

    /// <summary>
    /// Creates a validator using the real file system.
    /// </summary>
    public AppPathValidator()
        : this(File.Exists)
    {
    }

    /// <summary>
    /// Creates a validator with a custom existence probe, primarily for tests.
    /// </summary>
    public AppPathValidator(Func<string, bool> fileExists)
    {
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
    }

    /// <inheritdoc />
    public AppPathValidationResult Validate(string? executablePath)
    {
        if (!AppPathPolicy.TryValidatePath(executablePath, out var normalizedPath, out var reason))
        {
            return new AppPathValidationResult(false, normalizedPath, reason);
        }

        if (!_fileExists(normalizedPath!))
        {
            return new AppPathValidationResult(false, normalizedPath, "The application executable was not found.");
        }

        return new AppPathValidationResult(true, normalizedPath, null);
    }
}
