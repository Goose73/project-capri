using System.Text.RegularExpressions;

namespace Capri.Core.Voice;

/// <summary>
/// Wake phrase recognition is anchored: names mentioned incidentally must not
/// trigger the assistant. The separate question can be spoken after a pause
/// or as part of "Hey Capri, what is ...?".
/// </summary>
public static partial class WakeWordPolicy
{
    public const float WakeConfidence = 0.60f;
    public const float QuestionConfidence = 0.55f;

    public static bool IsWakePhrase(string? recognized, float confidence)
    {
        if (confidence < WakeConfidence || string.IsNullOrWhiteSpace(recognized))
            return false;
        var normalized = string.Join(" ", recognized.Trim().Trim(',', '.', '!', '?')
            .Replace(",", "", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        return normalized is "capri" or "hey capri";
    }

    /// <summary>
    /// Allow "Hey Capri, open chat" or "Capri what time is it" as a single
    /// utterance only when matched by the explicit wake-prefixed grammar.
    /// Never accept arbitrary dictation that merely contains Capri elsewhere.
    /// </summary>
    public static string? ExtractInlineQuestion(string? recognized, float confidence)
    {
        if (confidence < WakeConfidence ||
            string.IsNullOrWhiteSpace(recognized) ||
            recognized.Length > 850)
            return null;

        var match = InlineQuestionPattern().Match(recognized.Trim());
        if (!match.Success)
            return null;

        var question = match.Groups["question"].Value.Trim();
        if (question.Length is < 2 or > 800 || IsWakePhrase(question, 1f))
            return null;
        return question;
    }

    public static string? AcceptQuestion(
        string? recognized, float confidence, bool armed, DateTimeOffset armedUntil,
        DateTimeOffset now)
    {
        if (!armed || now > armedUntil || confidence < QuestionConfidence ||
            string.IsNullOrWhiteSpace(recognized))
            return null;
        var question = recognized.Trim();
        if (question.Length is < 2 or > 800 || IsWakePhrase(question, 1.0f))
            return null;
        return question;
    }

    [GeneratedRegex(@"^(?:hey[\s,]+capri|capri)[\s,.:;!?-]+(?<question>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineQuestionPattern();
}
