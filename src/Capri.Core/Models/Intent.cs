namespace Capri.Core.Models;

/// <summary>
/// Structured interpretation of a user utterance.
/// </summary>
/// <param name="Kind">The recognized action.</param>
/// <param name="Target">Optional target such as an application alias, URL, or search text.</param>
/// <param name="Media">Optional media command when <paramref name="Kind"/> is <see cref="IntentKind.MediaControl"/>.</param>
/// <param name="RawText">Original user text, retained for diagnostics.</param>
/// <param name="Confidence">Parser confidence between 0 and 1.</param>
public sealed record Intent(
    IntentKind Kind,
    string? Target = null,
    MediaCommand? Media = null,
    string? RawText = null,
    double Confidence = 1.0);
