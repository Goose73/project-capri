namespace Capri.Core.Abstractions;

/// <summary>
/// Well-known file system locations used by Capri.
/// </summary>
public interface ICapriPaths
{
    /// <summary>
    /// Root directory for application data.
    /// </summary>
    string Root { get; }

    /// <summary>
    /// Directory containing log files.
    /// </summary>
    string Logs { get; }

    /// <summary>
    /// Path to the settings JSON file.
    /// </summary>
    string SettingsFile { get; }

    /// <summary>
    /// Path to the protected credential file.
    /// </summary>
    string SecretsFile { get; }

    /// <summary>
    /// Directory containing conversation history files.
    /// </summary>
    string ConversationDir { get; }
}
