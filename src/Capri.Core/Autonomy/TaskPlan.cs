using System.Text.Json;

namespace Capri.Core.Autonomy;

public enum TaskPhase { Created, Planning, AwaitingPermission, Executing, Observing, Verifying, Completed, Failed, Cancelled }
public enum TaskTool { LaunchApp, OpenUrl, BraveSearch, CreateNote, OpenNote }

public sealed record TaskStep(TaskTool Tool, string Target, string? Text = null);
public sealed record TaskPlan(IReadOnlyList<TaskStep> Steps);
public sealed record TaskOutcome(bool Succeeded, bool Verified, string Message);
public sealed record TaskProgress(TaskPhase Phase, string Message);

/// <summary>Fail-closed grammar for untrusted model output. No executable paths or shell arguments.</summary>
public static class TaskPlanParser
{
    public static TaskPlan Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16000) throw new FormatException("Plan is empty or too long.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() is < 1 or > 8)
            throw new FormatException("Plan must contain 1–8 steps only.");
        var parsed = new List<TaskStep>();
        foreach (var step in steps.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object || !step.TryGetProperty("tool", out var tool) || tool.ValueKind != JsonValueKind.String ||
                !step.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.String)
                throw new FormatException("Invalid step.");
            var kind = tool.GetString() switch
            {
                "launch_app" => TaskTool.LaunchApp, "open_url" => TaskTool.OpenUrl,
                "brave_search" => TaskTool.BraveSearch, "create_note" => TaskTool.CreateNote,
                "open_note" => TaskTool.OpenNote, _ => throw new FormatException("Unknown tool.")
            };
            var fields = step.EnumerateObject().Select(p => p.Name).ToArray();
            if (fields.Distinct(StringComparer.Ordinal).Count() != fields.Length || fields.Any(f => f is not ("tool" or "target" or "text")))
                throw new FormatException("Unexpected step field.");
            var value = target.GetString()!;
            if (value.Length is < 1 or > 2048 || value.Any(char.IsControl)) throw new FormatException("Invalid target.");
            string? text = null;
            if (step.TryGetProperty("text", out var content))
            {
                if (kind != TaskTool.CreateNote || content.ValueKind != JsonValueKind.String) throw new FormatException("Unexpected text.");
                text = content.GetString();
                if (text is null || text.Length > 4096) throw new FormatException("Note too long.");
            }
            if (kind == TaskTool.CreateNote && text is null) throw new FormatException("Note text required.");
            if (kind is TaskTool.CreateNote or TaskTool.OpenNote && !SafeNoteName(value)) throw new FormatException("Unsafe note name.");
            if (kind == TaskTool.LaunchApp && value is not ("notepad" or "calculator" or "explorer" or "paint")) throw new FormatException("App not allowlisted.");
            if (kind == TaskTool.OpenUrl && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || value.Contains('@') || value.Contains('\\') || value.Any(char.IsWhiteSpace))) throw new FormatException("Only safe HTTPS URLs allowed.");
            if (kind == TaskTool.BraveSearch && value.Length > 200) throw new FormatException("Query too long.");
            parsed.Add(new TaskStep(kind, value, text));
        }
        // Notes may only be opened if created earlier in this same plan.
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in parsed)
        {
            if (step.Tool == TaskTool.CreateNote && !created.Add(step.Target)) throw new FormatException("Duplicate note.");
            if (step.Tool == TaskTool.OpenNote && !created.Contains(step.Target)) throw new FormatException("Note must be created first.");
        }
        return new TaskPlan(parsed);
    }

    public static bool SafeNoteName(string name) => name.Length is > 0 and <= 100 && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && name != ".txt" && name != "..txt" &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') && !name.Contains('\\') && !name.Contains(':') && !name.Contains("..", StringComparison.Ordinal) &&
        Path.GetFileName(name) == name;
}
