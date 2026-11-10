using Capri.Core.Autonomy.Authority;

namespace Capri.Desktop.Services;

/// <summary>
/// Bridges authority approval requests to the WPF UI. Implementations must run UI on the dispatcher
/// and never auto-approve. Returns null to deny/cancel; returns a decision only for explicit user approval.
/// </summary>
public interface ITaskAuthorityUiBridge
{
    Task<AuthorityApprovalDecision?> RequestApprovalAsync(ApprovalRequestSnapshot request, CancellationToken cancellationToken);
}

public sealed record AuthorityApprovalDecision(bool Approved, string? Workspace);
