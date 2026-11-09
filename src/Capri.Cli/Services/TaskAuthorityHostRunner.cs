using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;
using Capri.Security.Autonomy.Authority;

namespace Capri.Cli.Services;

/// <summary>Runs the per-user M5 task authority in-process (hosted by `capri authority serve`).</summary>
internal static class TaskAuthorityHostRunner
{
    public static async Task<int> ServeAsync(ICliServices services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var pipeName = NamedPipeTaskAuthorityClient.CurrentUserPipeName();
        var paths = services.Paths;
        var history = services.TaskHistory ?? throw new InvalidOperationException("Task history unavailable.");
        var provider = services.ChatProvider ?? throw new InvalidOperationException("Chat provider unavailable.");
        var router = services.ModelRouter ?? throw new InvalidOperationException("Model router unavailable.");
        var executor = services.TaskExecutor ?? throw new InvalidOperationException("Task executor unavailable.");
        var journal = services.TaskJournal ?? throw new InvalidOperationException("Task journal unavailable.");
        var control = services.TaskControl;

        await using var server = new NamedPipeTaskAuthorityServer(pipeName, paths, history, provider, router, executor, journal, control);
        try
        {
            server.Start();
        }
        catch (IOException)
        {
            // Another authority is already serving this user.
            return CliExitCodes.Success;
        }

        Console.Error.WriteLine($"capri: task authority serving ({pipeName}).");
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return CliExitCodes.Success;
    }
}
