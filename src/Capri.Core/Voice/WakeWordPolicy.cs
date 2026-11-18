namespace Capri.Core.Voice;

/// <summary>
/// Conservative wake-word and command-text policy independent of audio APIs.
/// It accepts only the user's two wake phrases, never a casual substring.
/// </summary>
public static class WakeWordPolicy
{
    public static bool IsWakePhrase(string? recognized, float confidence)
    {
        if (confidence < 0.65f || string.IsNullOrWhiteSpace(recognized))
            return false;
        var normalized = string.Join(" ", recognized.Trim().Trim(',', '.', '!', '?')
            .Replace(",", "", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        return normalized is "capri" or "hey capri";
    }

    public static string? AcceptQuestion(
        string? recognized, float confidence, bool armed, DateTimeOffset armedUntil,
        DateTimeOffset now)
    {
        if (!armed || now > armedUntil || confidence < 0.55f ||
            string.IsNullOrWhiteSpace(recognized))
            return null;
        var question = recognized.Trim();
        if (question.Length is < 2 or > 800 || IsWakePhrase(question, 1.0f))
            return null;
        return question;
    }
}
