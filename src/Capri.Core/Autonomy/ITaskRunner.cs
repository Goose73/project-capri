namespace Capri.Core.Autonomy;

/// <summary>
/// Runs multi-step tasks with progress reporting and emergency stop controls.
/// Implementations may execute locally or delegate to a shared authority.
/// </summary>
public interface ITaskRunner
{
    bool IsStopped { get; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Matches existing TaskCoordinator.Stop() API.")]
    void Stop();
    void ResetStop();
    IAsyncEnumerable<TaskProgress> RunAsync(string goal, CancellationToken cancellationToken = default);
}
