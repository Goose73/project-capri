using Capri.Cli.Services;
using Capri.Core.Autonomy;

namespace Capri.Cli.Commands;

/// <summary>
/// Read-only cross-process task observation. Polls the existing metadata journal;
/// it does not obtain execution ownership, authorize plans or dispatch actions.
/// --json writes one complete JSON object per change (NDJSON).
/// </summary>
public static class TaskWatch
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    public static async Task<int> ObserveAsync(
        Guid id,
        ICliServices services,
        ICliConsole console,
        bool json,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        if (id == Guid.Empty)
        {
            console.WriteErrorLine("capri: A nonempty task ID is required.");
            return CliExitCodes.Usage;
        }

        var control = services.TaskControl;
        var history = services.TaskHistory;
        if (control is null || history is null)
        {
            console.WriteErrorLine("capri: Task observation services are unavailable.");
            return CliExitCodes.Error;
        }

        string? lastSnapshot = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = control.GetActive();
            var stop = control.GetStopState();
            var entry = history.Find(id);
            var ownsActive = active?.TaskId == id;
            if (entry is null && !ownsActive)
            {
                if (lastSnapshot is null)
                {
                    console.WriteErrorLine("capri: Task ID not found in local metadata. Start the task or check its ID.");
                    return CliExitCodes.Error;
                }

                console.WriteErrorLine("capri: Task ownership and metadata disappeared. Outcome unknown; no automatic replay.");
                return CliExitCodes.Error;
            }

            var snapshot = JsonOutput.Serialize(new
            {
                taskId = id,
                active = ownsActive,
                processId = ownsActive ? active!.ProcessId : (int?)null,
                stopped = stop.Stopped,
                stopGeneration = stop.Generation,
                metadata = entry is null ? null : TaskCommand.SafeTaskDto(entry),
            });

            if (!string.Equals(lastSnapshot, snapshot, StringComparison.Ordinal))
            {
                if (json)
                {
                    console.WriteLine(snapshot);
                }
                else
                {
                    var phase = entry?.Phase ?? "Starting";
                    var step = entry is null ? string.Empty :
                        $" step {entry.StepIndex}{(entry.TotalSteps is int total ? "/" + total : string.Empty)}";
                    var verification = entry is null ? string.Empty :
                        $" verified={Display(entry.LastStepVerified)} dispatched={Display(entry.LastStepDispatched)}";
                    console.WriteLine($"{id}: {phase}{step}, active={ownsActive}, stop={stop.Stopped}{verification}");
                }

                lastSnapshot = snapshot;
            }

            // A terminal journal phase without process ownership means the task has
            // finished. The historical "Completed" phase still distinguishes
            // verified steps from dispatched-only external actions.
            if (!ownsActive && entry is not null && IsTerminal(entry.Phase))
                return CliExitCodes.Success;

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTerminal(string phase) =>
        phase is "Completed" or "Failed" or "Cancelled";

    private static string Display(bool? value) =>
        value is null ? "unknown" : value.Value ? "yes" : "no";
}
