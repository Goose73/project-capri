using Capri.Core.Models;

namespace Capri.Core.Abstractions.Automation;

/// <summary>
/// Opens web pages and performs web searches.
/// </summary>
public interface IWebLauncher
{
    /// <summary>
    /// Opens the supplied URI in the default browser.
    /// </summary>
    Task<AutomationResult> OpenAsync(Uri uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the default search engine for <paramref name="query"/>.
    /// </summary>
    Task<AutomationResult> SearchAsync(string query, CancellationToken cancellationToken = default);
}
