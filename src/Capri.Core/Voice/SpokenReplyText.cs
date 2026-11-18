using System.Text;

namespace Capri.Core.Voice;

/// <summary>
/// Removes code/formatting before local narration. Speaking literal Markdown,
/// giant code blocks and URLs is unpleasant and risks reading private data
/// unnecessarily. The original answer remains visible in full in chat.
/// </summary>
public static class SpokenReplyText
{
    public static string Prepare(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return string.Empty;
        var result = new StringBuilder();
        var inCode = false;
        var codeMentioned = false;
        foreach (var raw in answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                if (!codeMentioned)
                {
                    Append("I've included code in the chat.");
                    codeMentioned = true;
                }
                continue;
            }

            if (inCode || line.Length == 0) continue;
            line = line.TrimStart('#', '-', '*', ' ', '\t');
            line = line.Replace("**", "", StringComparison.Ordinal)
                .Replace("`", "", StringComparison.Ordinal)
                .Replace("__", "", StringComparison.Ordinal);
            if (line.Length == 0) continue;
            if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                line = "I've included a link in the chat.";
            Append(line);
            if (result.Length > 700) break;
        }
        var output = result.ToString().Trim();
        return output.Length > 750 ? output[..750].TrimEnd() + "..." : output;

        void Append(string sentence)
        {
            if (result.Length > 0) result.Append(' ');
            result.Append(sentence);
            if (sentence[^1] is not ('.' or '!' or '?' or ':' or ';'))
                result.Append('.');
        }
    }
}
