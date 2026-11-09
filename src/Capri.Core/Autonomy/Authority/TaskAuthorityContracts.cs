using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capri.Core.Autonomy.Authority;

/// <summary>Wire protocol for the per-user Capri task authority (named pipe, JSON frames). No secrets.</summary>
public static class TaskAuthorityProtocol
{
    public const int MaxFrameBytes = 256 * 1024;
    public const string PipePrefix = "capri.task-authority.";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Builds a deterministic pipe name from a stable per-user identifier (e.g. SID). Callers must supply the current-user SID on Windows.</summary>
    public static string PipeNameForUserIdentifier(string userIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentifier);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userIdentifier)))[..16].ToLowerInvariant();
        return PipePrefix + hash;
    }

    public static string ComputePlanHash(TaskPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var canonical = CanonicalizePlan(plan);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string CanonicalizePlan(TaskPlan plan)
    {
        var dto = new PlanDto(plan.Steps.Select(s => new StepDto(ToolName(s.Tool), s.Target, s.Text)).ToArray());
        return JsonSerializer.Serialize(dto, Json);
    }

    private static string ToolName(TaskTool tool) => tool switch
    {
        TaskTool.LaunchApp => "launch_app",
        TaskTool.OpenUrl => "open_url",
        TaskTool.BraveSearch => "brave_search",
        TaskTool.CreateNote => "create_note",
        TaskTool.OpenNote => "open_note",
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    private sealed record PlanDto(StepDto[] Steps);
    private sealed record StepDto(string Tool, string Target, string? Text);
}

public enum AuthorityRequestKind
{
    Hello,
    GetState,
    ListTasks,
    GetTask,
    SubmitTask,
    Approve,
    Deny,
    CancelTask,
    Stop,
    ResetStop,
    Subscribe,
    Unsubscribe,
    Ping,
    Shutdown // test/controlled only; authority may ignore from non-owner contexts
}

public enum AuthorityEventKind
{
    StateChanged,
    TaskProgress,
    ApprovalRequired,
    ApprovalResolved,
    TaskCompleted,
    Subscribed
}

public sealed record AuthorityRequest(
    string RequestId,
    AuthorityRequestKind Kind,
    string? Goal = null,
    Guid? TaskId = null,
    Guid? ApprovalId = null,
    string? PlanHash = null,
    string? Workspace = null,
    int? MaxCount = null,
    string? ClientName = null,
    bool? CanApprove = null);

public sealed record AuthorityResponse(
    string RequestId,
    bool Ok,
    string? Error = null,
    AuthorityStateSnapshot? State = null,
    TaskSnapshot? Task = null,
    IReadOnlyList<TaskHistoryEntry>? Tasks = null);

public sealed record AuthorityEvent(
    AuthorityEventKind Kind,
    AuthorityStateSnapshot? State = null,
    TaskSnapshot? Task = null,
    ApprovalRequestSnapshot? Approval = null,
    TaskProgress? Progress = null);

/// <summary>Unified wire frame: exactly one of Response or Event is set.</summary>
public sealed record AuthorityEnvelope(AuthorityResponse? Response = null, AuthorityEvent? Event = null);

public sealed record TaskSnapshot(
    Guid TaskId,
    string Goal,
    string Phase,
    int StepIndex,
    int? TotalSteps,
    string? ProviderId,
    string? ModelId,
    string? Message,
    bool? LastVerified,
    bool? LastDispatched,
    bool HasPendingApproval,
    DateTimeOffset UpdatedUtc);

public sealed record ApprovalRequestSnapshot(
    Guid TaskId,
    Guid ApprovalId,
    string Goal,
    string? ProviderId,
    string? ModelId,
    TaskPlan Plan,
    string PlanHash,
    DateTimeOffset RequestedUtc);

public sealed record AuthorityStateSnapshot(
    bool Stopped,
    long StopGeneration,
    TaskSnapshot? ActiveTask,
    ApprovalRequestSnapshot? PendingApproval,
    DateTimeOffset UpdatedUtc);

/// <summary>Client abstraction for CLI/GUI/voice. Implementations talk to the local authority only.</summary>
public interface ITaskAuthorityClient : IAsyncDisposable
{
    Task<AuthorityResponse> SendAsync(AuthorityRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AuthorityEvent> Events(CancellationToken cancellationToken = default);
    ValueTask EnsureConnectedAsync(CancellationToken cancellationToken = default);
    bool IsConnected { get; }
}
