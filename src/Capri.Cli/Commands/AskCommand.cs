using System.Diagnostics;
using System.Text;
using Capri.Cli.Services;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Cli.Commands;

/// <summary>
/// Sends a single question to the configured chat provider.
/// </summary>
public sealed class AskCommand
{
    private readonly ICliConsole _console;
    private readonly ICliServices _services;

    /// <summary>Creates the command.</summary>
    public AskCommand(ICliConsole console, ICliServices services)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Executes <c>capri ask</c>.</summary>
    public async Task<int> ExecuteAsync(CliInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        var question = invocation.Arguments.Count > 0 ? invocation.Arguments[0] : string.Empty;
        if (string.IsNullOrWhiteSpace(question))
        {
            return Usage("A question is required. Usage: capri ask \"<question>\" [--json]");
        }

        if (!ChatCommandSupport.TryGetConfiguredChat(_services, out var provider, out var router, out var configError))
        {
            return ReportError(configError!, invocation.Json);
        }

        var route = await router!.ResolveAsync(new ModelRouteRequest(), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(route.ModelId))
        {
            return ReportError(
                "No chat model is configured. Use `capri models use <model>` or `capri config set provider.model <model>`.",
                invocation.Json);
        }

        var request = new ChatRequest(
            [new ChatMessage(ChatRole.User, question)],
            ModelId: route.ModelId);

        var stopwatch = Stopwatch.StartNew();
        var sb = new StringBuilder();
        ProviderError? streamError = null;

        try
        {
            if (route.SupportsStreaming)
            {
                await foreach (var delta in provider!.StreamAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (delta.Error is not null)
                    {
                        streamError = delta.Error;
                        break;
                    }

                    if (!string.IsNullOrEmpty(delta.Content))
                    {
                        sb.Append(delta.Content);
                        if (!invocation.Json)
                        {
                            _console.Write(delta.Content);
                        }
                    }
                }
            }
            else
            {
                var completion = await provider!.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
                if (completion.Error is not null)
                {
                    streamError = completion.Error;
                }
                else
                {
                    sb.Append(completion.Message.Content ?? string.Empty);
                    if (!invocation.Json && sb.Length > 0)
                    {
                        _console.Write(sb.ToString());
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            streamError = new ProviderError(ProviderErrorKind.Unknown, CliErrors.Describe(exception));
        }

        if (!invocation.Json && sb.Length > 0 && !sb.ToString().EndsWith('\n'))
        {
            _console.WriteLine(string.Empty);
        }

        stopwatch.Stop();

        if (streamError is not null)
        {
            return ReportProviderError(streamError, invocation.Json, route, stopwatch.Elapsed);
        }

        if (invocation.Json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                ok = true,
                providerId = route.ProviderId,
                model = route.ModelId,
                latencyMs = (int)Math.Round(stopwatch.Elapsed.TotalMilliseconds),
                text = sb.ToString(),
            }));
        }

        return CliExitCodes.Success;
    }

    private int ReportProviderError(ProviderError error, bool json, ModelRoute route, TimeSpan elapsed)
    {
        var message = ChatCommandSupport.DescribeProviderError(error, _services.Settings.Current.Provider);
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                ok = false,
                providerId = route.ProviderId,
                model = route.ModelId,
                latencyMs = (int)Math.Round(elapsed.TotalMilliseconds),
                error = new { kind = error.Kind.ToString(), message },
            }));
            return CliExitCodes.Error;
        }

        _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Error;
    }

    private int ReportError(string message, bool json)
    {
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = false, error = new { message } }));
            return CliExitCodes.Error;
        }

        _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Error;
    }

    private int Usage(string message)
    {
        _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Usage;
    }
}

/// <summary>
/// Shared helpers for CLI chat/ask/provider-test commands.
/// </summary>
internal static class ChatCommandSupport
{
    public static bool TryGetConfiguredChat(
        ICliServices services,
        out IChatProvider? provider,
        out IModelRouter? router,
        out string? error)
    {
        provider = services.ChatProvider;
        router = services.ModelRouter;
        error = null;

        if (provider is null || router is null)
        {
            error = "Chat services are unavailable in this CLI build. Rebuild the CLI or run from a complete installation.";
            return false;
        }

        var settings = services.Settings.Current.Provider;
        if (string.IsNullOrWhiteSpace(settings.ProviderId) && string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            error = "No provider is configured. Use `capri models use <model>` for local Ollama or `capri config` to configure a provider.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.ChatModel))
        {
            error = "No chat model is configured. Use `capri models list`, `capri models use <model>`, or `capri config set provider.model <model>`.";
            return false;
        }

        return true;
    }

    public static string DescribeProviderError(ProviderError error, AppSettings.ProviderSettings provider)
    {
        var safe = error.Message ?? "The provider request failed.";
        return error.Kind switch
        {
            ProviderErrorKind.Configuration when safe.Contains("No chat model", StringComparison.OrdinalIgnoreCase)
                => "No chat model is configured. Use `capri models use <model>` or `capri config set provider.model <model>`.",
            ProviderErrorKind.Configuration when safe.Contains("Cloud inference is disabled", StringComparison.OrdinalIgnoreCase)
                => safe + " (You can enable cloud consent in settings only if you intentionally want cloud inference.)",
            ProviderErrorKind.Configuration when safe.Contains("Free models only", StringComparison.OrdinalIgnoreCase)
                => safe,
            ProviderErrorKind.Authentication => "Provider authentication failed. Check credentials in the desktop Settings window (CLI never displays keys).",
            ProviderErrorKind.Authorization => "Provider authorization failed. Check account/model access and credentials.",
            ProviderErrorKind.Timeout => "The provider request timed out. Check that the provider is running and responsive.",
            ProviderErrorKind.Network => IsLikelyLocal(provider)
                ? $"Could not reach the local provider at {OllamaClient.GetApiRoot(provider.BaseUrl)}. Start Ollama/LM Studio and verify the base URL."
                : "Could not reach the provider. Check network connectivity and base URL.",
            ProviderErrorKind.NotFound => IsLikelyLocal(provider)
                ? $"The provider or model was not found. Verify the model is installed (`capri models list`) and selected (`capri models use <model>`). Details: {safe}"
                : $"The provider endpoint or model was not found. Details: {safe}",
            ProviderErrorKind.RateLimited => "The provider rate-limited the request. Try again later.",
            ProviderErrorKind.InvalidRequest => $"The provider rejected the request. Details: {safe}",
            ProviderErrorKind.ServerError => $"The provider reported a server error. Details: {safe}",
            _ => safe,
        };
    }

    private static bool IsLikelyLocal(AppSettings.ProviderSettings provider)
    {
        return Capri.Core.Providers.ProviderCatalog.IsLocal(provider.ProviderId)
            || Capri.Core.Providers.ProviderCatalog.IsLoopbackAllowedHttp(provider.BaseUrl);
    }
}
