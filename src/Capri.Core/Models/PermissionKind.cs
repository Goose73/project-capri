namespace Capri.Core.Models;

/// <summary>
/// Category of action that may require user permission.
/// </summary>
public enum PermissionKind
{
    Unknown = 0,
    LaunchApplication,
    OpenWeb,
    MediaControl,
    FileAccess,
    RunCommand
}
