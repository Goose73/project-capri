namespace Capri.Core.Models;

/// <summary>Bounded tracked-worktree status text. Never posted to AI automatically.</summary>
public sealed record DeveloperGitStatus(Guid SessionId, string Output);

/// <summary>Bounded, separately labelled staged/unstaged diffs for one approved source file.</summary>
public sealed record DeveloperGitDiff(
    Guid SessionId, string RelativePath, string StagedDiff, string UnstagedDiff);
