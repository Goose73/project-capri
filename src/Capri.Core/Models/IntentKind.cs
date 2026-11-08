namespace Capri.Core.Models;

/// <summary>
/// High-level action recognized in user input.
/// </summary>
public enum IntentKind
{
    Unknown = 0,
    Chat,
    LaunchApp,
    OpenWeb,
    MediaControl
}
