using System.Diagnostics;
using Capri.Automation.Apps;
using Capri.Automation.Media;
using Capri.Automation.Processes;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Models;

namespace Capri.Tests.Automation;

/// <summary>
/// Records process start requests without launching anything.
/// </summary>
internal sealed class RecordingProcessStarter : IProcessStarter
{
    public List<ProcessStartInfo> Requests { get; } = [];

    public bool Result { get; set; } = true;

    public string? FailureReason { get; set; }

    public bool TryStart(ProcessStartInfo startInfo, out string? failureReason)
    {
        Requests.Add(startInfo);
        failureReason = Result ? null : FailureReason ?? "The process could not be started.";
        return Result;
    }
}

/// <summary>
/// Returns a caller-supplied validation result.
/// </summary>
internal sealed class StubAppPathValidator : IAppPathValidator
{
    public AppPathValidationResult Result { get; set; } =
        new(false, null, "The path is not permitted.");

    public string? LastPath { get; private set; }

    public AppPathValidationResult Validate(string? executablePath)
    {
        LastPath = executablePath;
        return Result;
    }
}

/// <summary>
/// In-memory alias resolver that never touches the real alias table.
/// </summary>
internal sealed class StubAppAliasResolver : IAppAliasResolver
{
    private readonly Dictionary<string, AppTarget> _targets = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string alias, AppTarget target)
    {
        _targets[alias] = target;
    }

    public ValueTask<AppTarget?> ResolveAsync(
        string applicationAlias,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            _targets.TryGetValue(applicationAlias, out var target) ? target : null);
    }
}

/// <summary>
/// Records media virtual keys without calling SendInput.
/// </summary>
internal sealed class RecordingMediaKeySender : IMediaKeySender
{
    public List<int> SentVirtualKeys { get; } = [];

    public bool Result { get; set; } = true;

    public string? FailureReason { get; set; }

    public bool TrySend(int virtualKey, out string? failureReason)
    {
        SentVirtualKeys.Add(virtualKey);
        failureReason = Result ? null : FailureReason ?? "The media key could not be sent.";
        return Result;
    }
}
