using System.Text;
using Capri.Cli.Services;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Cli.Commands;

/// <summary>
/// Interactive multi-turn terminal chat with the configured provider.
/// </summary>
public sealed class ChatCommand
{
    private readonly ICliConsole _console;
    private readonly ICliServices _services;

    /// <summary>Creates the command.</summary>
    public ChatCommand(ICliConsole console, ICliServices services)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Runs the interactive chat session.</summary>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_console.IsInteractive)
        {
            _console.WriteErrorLine("capri: `capri chat` requires an interactive terminal. Use `capri ask \"<question>\"` for non-interactive use.");
            return CliExitCodes.Usage;
        }

        if (!ChatCommandSupport.TryGetConfiguredChat(_services, out var provider, out var router, out var configError))
        {
            _console.WriteErrorLine($"capri: {configError}");
            return CliExitCodes.Error;
        }

        var route = await router!.ResolveAsync(new ModelRouteRequest(), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(route.ModelId))
        {
            _console.WriteErrorLine("capri: No chat model is configured. Use `capri models use <model>` or `capri config set provider.model <model>`.");
            return CliExitCodes.Error;
        }

        _console.WriteLine($"Capri chat - provider: {route.ProviderId}, model: {route.ModelId}");
        _console.WriteLine("Commands: /exit, /cancel (during generation via Ctrl+C when supported).");
        _console.WriteLine(string.Empty);

        var history = new List<ChatMessage>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _console.Write("you> ");
            var line = _console.ReadLine();
            if (line is null)
            {
                _console.WriteLine(string.Empty);
                return CliExitCodes.Success;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (IsExit(trimmed))
            {
                return CliExitCodes.Success;
            }

            if (string.Equals(trimmed, "/cancel", StringComparison.OrdinalIgnoreCase))
            {
                _console.WriteLine("No generation in progress.");
                continue;
            }

            history.Add(new ChatMessage(ChatRole.User, line));
            _console.Write("capri> ");

            var (response, cancelled, error) = await GenerateAsync(
                provider!, route, history, cancellationToken).ConfigureAwait(false);

            if (cancelled)
            {
                _console.WriteLine(string.Empty + "[generation cancelled]");
                if (response.Length > 0)
                {
                    history.Add(new ChatMessage(ChatRole.Assistant, response));
                }

                continue;
            }

            if (error is not null)
            {
                _console.WriteLine(string.Empty);
                _console.WriteErrorLine($"capri: {ChatCommandSupport.DescribeProviderError(error, _services.Settings.Current.Provider)}");
                // Keep user message in history? Removing it avoids repeated bad state; leave it - user can continue.
                continue;
            }

            _console.WriteLine(string.Empty);
            history.Add(new ChatMessage(ChatRole.Assistant, response));
        }
    }

    private async Task<(string Response, bool Cancelled, ProviderError? Error)> GenerateAsync(
        IChatProvider provider,
        ModelRoute route,
        IReadOnlyList<ChatMessage> history,
        CancellationToken sessionToken)
    {
        using var generationCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        var request = new ChatRequest([.. history], ModelId: route.ModelId);
        var sb = new StringBuilder();
        var originalTreatAsInput = _console.TreatControlCAsInput;
        ProviderError? error = null;
        var cancelledByUser = false;

        try
        {
            if (_console.IsInteractive)
            {
                _console.TreatControlCAsInput = true;
            }

            if (route.SupportsStreaming)
            {
                await foreach (var delta in provider.StreamAsync(request, generationCts.Token).ConfigureAwait(false))
                {
                    if (TryConsumeCancel(generationCts))
                    {
                        cancelledByUser = true;
                        break;
                    }

                    if (delta.Error is not null)
                    {
                        error = delta.Error;
                        break;
                    }

                    if (!string.IsNullOrEmpty(delta.Content))
                    {
                        sb.Append(delta.Content);
                        _console.Write(delta.Content);
                    }
                }
            }
            else
            {
                // For non-streaming, check for cancel before/after; cannot interrupt in-flight HTTP easily without CTS.
                if (TryConsumeCancel(generationCts))
                {
                    cancelledByUser = true;
                }
                else
                {
                    var completionTask = provider.CompleteAsync(request, generationCts.Token);
                    while (!completionTask.IsCompleted)
                    {
                        if (TryConsumeCancel(generationCts))
                        {
                            cancelledByUser = true;
                            break;
                        }

                        await Task.WhenAny(completionTask, Task.Delay(50, sessionToken)).ConfigureAwait(false);
                    }

                    if (!cancelledByUser)
                    {
                        var completion = await completionTask.ConfigureAwait(false);
                        if (completion.Error is not null)
                        {
                            error = completion.Error;
                        }
                        else
                        {
                            sb.Append(completion.Message.Content ?? string.Empty);
                            _console.Write(sb.ToString());
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!sessionToken.IsCancellationRequested)
        {
            cancelledByUser = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            error = new ProviderError(ProviderErrorKind.Unknown, CliErrors.Describe(exception));
        }
        finally
        {
            try
            {
                _console.TreatControlCAsInput = originalTreatAsInput;
            }
            catch
            {
            }

            DrainPendingKeys();
        }

        return (sb.ToString(), cancelledByUser, error);
    }

    private bool TryConsumeCancel(CancellationTokenSource generationCts)
    {
        if (!_console.KeyAvailable)
        {
            return false;
        }

        try
        {
            var key = _console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                try { generationCts.Cancel(); } catch (ObjectDisposedException) { }
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private void DrainPendingKeys()
    {
        try
        {
            while (_console.KeyAvailable)
            {
                _ = _console.ReadKey(intercept: true);
            }
        }
        catch
        {
        }
    }

    private static bool IsExit(string value)
    {
        return string.Equals(value, "/exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/quit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "quit", StringComparison.OrdinalIgnoreCase);
    }
}
