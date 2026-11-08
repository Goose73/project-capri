using Capri.Core.Abstractions;

namespace Capri.Security;

/// <summary>
/// Default file system layout for Capri application data.
/// </summary>
/// <remarks>
/// Paths are resolved eagerly, but directories are only created when a caller
/// (or a store) explicitly needs them. Set <c>CAPRI_DATA_DIR</c> to relocate the
/// data root, which is useful for tests and portable deployments.
/// </remarks>
public sealed class CapriPaths : ICapriPaths
{
    /// <summary>Environment variable that overrides the data root directory.</summary>
    public const string DataDirectoryEnvironmentVariable = "CAPRI_DATA_DIR";

    /// <summary>Folder name used below the local application data directory.</summary>
    public const string ApplicationFolderName = "Capri";

    /// <summary>Name of the settings file.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>Name of the protected credential file.</summary>
    public const string SecretsFileName = "secrets.dat";

    /// <summary>Name of the log directory.</summary>
    public const string LogsDirectoryName = "Logs";

    /// <summary>Name of the conversation history directory.</summary>
    public const string ConversationsDirectoryName = "conversations";

    /// <summary>
    /// Creates paths using <c>CAPRI_DATA_DIR</c> when set, otherwise
    /// <c>%LOCALAPPDATA%\Capri</c>.
    /// </summary>
    public CapriPaths()
        : this(ResolveRootDirectory())
    {
    }

    /// <summary>
    /// Creates paths rooted at an explicit directory. Intended for tests and
    /// callers that manage their own data location.
    /// </summary>
    /// <param name="rootDirectory">Absolute or relative root directory.</param>
    public CapriPaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A data root directory is required.", nameof(rootDirectory));
        }

        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory.Trim()));
        Logs = Path.Combine(Root, LogsDirectoryName);
        SettingsFile = Path.Combine(Root, SettingsFileName);
        SecretsFile = Path.Combine(Root, SecretsFileName);
        ConversationDir = Path.Combine(Root, ConversationsDirectoryName);
    }

    /// <inheritdoc />
    public string Root { get; }

    /// <inheritdoc />
    public string Logs { get; }

    /// <inheritdoc />
    public string SettingsFile { get; }

    /// <inheritdoc />
    public string SecretsFile { get; }

    /// <inheritdoc />
    public string ConversationDir { get; }

    /// <summary>
    /// Creates the root, log, and conversation directories if they do not exist.
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(ConversationDir);
    }

    /// <summary>
    /// Resolves the configured data root without touching the file system.
    /// </summary>
    public static string ResolveRootDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return overrideDirectory.Trim();
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            // Extremely unlikely, but keeps the application usable in constrained environments.
            localApplicationData = AppContext.BaseDirectory;
        }

        return Path.Combine(localApplicationData, ApplicationFolderName);
    }
}
