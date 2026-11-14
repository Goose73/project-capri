namespace Capri.Core.Models;

/// <summary>Single bounded, untrusted filesystem entry in an approved project folder.</summary>
public sealed record DeveloperWorkspaceItem(string RelativePath, string Name, bool IsDirectory);
