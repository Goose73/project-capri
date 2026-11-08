using System.Diagnostics;
using Capri.Cli.Services;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Cli.Commands;

/// <summary>
/// Provider operations such as live inference testing.
/// </summary>
public sealed class ProvidersCommand
{
    private const string ProbePrompt = "Reply with: Capri ready";

    private readonly ICliConsole _console;
    private readonly ICliServices _services;

    /// <summary>Creates the command.</summary>
    public ProvidersCommand(ICliConsole console, ICliServices services)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Executes <c>capri providers</c>.</summary>
    public async Task<int> ExecuteAsync(CliInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (invocation.Arguments.Count == 0 || !string.Equals(invocation.Arguments[0], "test", StringComparison.OrdinalIgnoreCase))
        {
            _console.WriteErrorLine("capri: Usage: capri providers test [--json]");
            return CliExitCodes.Usage;
        }

        if (!ChatCommandSupport.TryGetConfiguredChat(_services, out var provider, out var router, out var configError))
        {
            return WriteResult(invocation.Json, success: false, route: null, elapsed: TimeSpan.Zero, message: configError, responseText: null, errorKind: "configuration");
        }

        var route = await router!.ResolveAsync(new ModelRouteRequest(), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(route.ModelId))
        {
            return WriteResult(invocation.Json, false, route, TimeSpan.Zero,
                "No chat model is configured. Use `capri models use <model>` or `capri config set provider.model <model>`.",
                null, "configuration");
        }

        var request = new ChatRequest(
            [new ChatMessage(ChatRole.User, ProbePrompt)],
            ModelId: route.ModelId,
            Temperature: 0,
            MaxOutputTokens: 32);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var completion = await provider!.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (completion.Error is not null)
            {
                var message = ChatCommandSupport.DescribeProviderError(completion.Error, _services.Settings.Current.Provider);
                return WriteResult(invocation.Json, false, route, stopwatch.Elapsed, message, null, completion.Error.Kind.ToString());
            }

            var text = completion.Message.Content?.Trim() ?? string.Empty;
            var ready = text.Contains("Capri ready", StringComparison.OrdinalIgnoreCase);
            var message2 = ready
                ? "Provider inference succeeded."
                : "Provider inference succeeded, but the response did not match the exact probe text.";
            return WriteResult(invocation.Json, true, route, stopwatch.Elapsed, message2, text, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return WriteResult(invocation.Json, false, route, stopwatch.Elapsed, CliErrors.Describe(exception), null, "unknown");
        }
    }

    private int WriteResult(
        bool json,
        bool success,
        ModelRoute? route,
        TimeSpan elapsed,
        string? message,
        string? responseText,
        string? errorKind)
    {
        var latencyMs = (int)Math.Round(elapsed.TotalMilliseconds);
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                ok = success,
                providerId = route?.ProviderId ?? _services.Settings.Current.Provider.ProviderId,
                model = route?.ModelId ?? _services.Settings.Current.Provider.ChatModel,
                latencyMs,
                probe = ProbePrompt,
                message,
                response = responseText,
                error = success ? null : new { kind = errorKind, message },
            }));
            return success ? CliExitCodes.Success : CliExitCodes.Error;
        }

        if (success)
        {
            _console.WriteLine($"Provider test: ok ({latencyMs} ms)");
            _console.WriteLine($"Provider: {route?.ProviderId}");
            _console.WriteLine($"Model: {route?.ModelId}");
            if (!string.IsNullOrWhiteSpace(message))
            {
                _console.WriteLine(message);
            }

            if (!string.IsNullOrWhiteSpace(responseText))
            {
                _console.WriteLine($"Response: {responseText}");
            }

            return CliExitCodes.Success;
        }

        _console.WriteErrorLine($"capri: provider test failed ({latencyMs} ms): {message}");
        return CliExitCodes.Error;
    }
}
