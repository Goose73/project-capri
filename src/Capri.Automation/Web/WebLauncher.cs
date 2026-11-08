using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Capri.Automation.Processes;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Models;

namespace Capri.Automation.Web;

/// <summary>
/// Opens validated web addresses with the default browser and performs web searches.
/// </summary>
public sealed class WebLauncher : IWebLauncher
{
    /// <summary>
    /// Search endpoint used when an utterance requests a web search.
    /// </summary>
    public static readonly Uri DefaultSearchBaseUri = new("https://www.bing.com/search");

    private readonly IProcessStarter _processStarter;

    /// <summary>
    /// Creates the launcher.
    /// </summary>
    public WebLauncher(IProcessStarter processStarter)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    /// <inheritdoc />
    public Task<AutomationResult> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new AutomationResult(false, "The web request was cancelled."));
        }

        if (uri is null)
        {
            return Task.FromResult(new AutomationResult(false, "A web address is required."));
        }

        // Re-validate even though callers normally validate first; this keeps the
        // launcher safe when used directly.
        var validation = UrlPolicy.Validate(uri.AbsoluteUri);
        if (!validation.IsValid)
        {
            return Task.FromResult(new AutomationResult(false, validation.Reason));
        }

        return Task.FromResult(Start(validation.Uri!));
    }

    /// <inheritdoc />
    public Task<AutomationResult> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new AutomationResult(false, "The web request was cancelled."));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new AutomationResult(false, "A search query is required."));
        }

        var searchUri = new Uri(
            DefaultSearchBaseUri,
            "?q=" + Uri.EscapeDataString(query.Trim()));

        return Task.FromResult(Start(searchUri));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Browser launch failures must be surfaced as an automation result.")]
    private AutomationResult Start(Uri uri)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            };

            return _processStarter.TryStart(startInfo, out var failureReason)
                ? new AutomationResult(true, $"Opened {uri.Host}.")
                : new AutomationResult(false, failureReason ?? "The web address could not be opened.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException)
        {
            return new AutomationResult(false, exception.Message);
        }
    }
}
