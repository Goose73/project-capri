using Capri.Core.Abstractions;
using Capri.Core.Autonomy;

namespace Capri.Cli.Services;

/// <summary>
/// Shared backend services needed by CLI commands. The default implementation
/// is composed from the same registration helpers as the desktop app.
/// </summary>
public interface ICliServices : IDisposable
{
    /// <summary>Well-known Capri paths.</summary>
    ICapriPaths Paths { get; }

    /// <summary>Persisted application settings.</summary>
    IAppSettingsStore Settings { get; }

    /// <summary>OS-protected credential store. Values are never displayed.</summary>
    ISecureCredentialStore Credentials { get; }

    /// <summary>Configured chat provider, or null when AI services are unavailable.</summary>
    IChatProvider? ChatProvider => null;

    /// <summary>Configured model router, or null when AI services are unavailable.</summary>
    IModelRouter? ModelRouter => null;

    /// <summary>HTTP client factory for provider/local API calls, or null when unavailable.</summary>
    IHttpClientFactory? HttpClientFactory => null;

    /// <summary>Local Ollama API client, or null when unavailable.</summary>
    IOllamaClient? OllamaClient => null;

    ITaskControl? TaskControl => null;
    ITaskHistoryStore? TaskHistory => null;
    ITaskExecutor? TaskExecutor => null;
    ITaskJournal? TaskJournal => null;
}
