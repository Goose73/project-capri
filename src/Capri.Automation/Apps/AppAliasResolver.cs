using Capri.Core.Abstractions.Automation;
using Capri.Core.Models;

namespace Capri.Automation.Apps;

/// <summary>
/// Resolves a small, curated set of safe application aliases to absolute
/// executable paths. Unknown or path-like aliases are rejected.
/// </summary>
public sealed class AppAliasResolver : IAppAliasResolver
{
    private readonly IReadOnlyDictionary<string, AppTarget> _aliases;

    /// <summary>
    /// Creates a resolver using the built-in safe alias set.
    /// </summary>
    public AppAliasResolver()
        : this(CreateDefaultAliases())
    {
    }

    /// <summary>
    /// Creates a resolver with an explicit alias map, primarily for tests.
    /// </summary>
    public AppAliasResolver(IReadOnlyDictionary<string, AppTarget> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        _aliases = aliases;
    }

    /// <inheritdoc />
    public ValueTask<AppTarget?> ResolveAsync(
        string applicationAlias,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var alias = NormalizeAlias(applicationAlias);
        if (alias is null)
        {
            return ValueTask.FromResult<AppTarget?>(null);
        }

        return ValueTask.FromResult(_aliases.TryGetValue(alias, out var target) ? target : null);
    }

    /// <summary>
    /// Builds the built-in alias map from well-known Windows locations.
    /// </summary>
    public static IReadOnlyDictionary<string, AppTarget> CreateDefaultAliases()
    {
        var aliases = new Dictionary<string, AppTarget>(StringComparer.OrdinalIgnoreCase);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system32 = string.IsNullOrWhiteSpace(windows) ? null : Path.Combine(windows, "System32");

        Add(aliases, ["notepad"], "Notepad", system32, "notepad.exe", "notepad");
        Add(aliases, ["calculator", "calc"], "Calculator", system32, "calc.exe", "calc");
        Add(aliases, ["explorer", "file explorer", "files"], "File Explorer", windows, "explorer.exe", "explorer");
        Add(aliases, ["paint", "mspaint"], "Paint", system32, "mspaint.exe", "mspaint");

        return aliases;
    }

    /// <summary>
    /// Normalizes a spoken alias. Returns null for aliases that look like paths
    /// or environment-variable expressions.
    /// </summary>
    private static string? NormalizeAlias(string? applicationAlias)
    {
        if (string.IsNullOrWhiteSpace(applicationAlias))
        {
            return null;
        }

        var alias = applicationAlias.Trim();

        if (alias.Length > 64
            || alias.Any(char.IsControl)
            || alias.Contains('\\')
            || alias.Contains('/')
            || alias.Contains(':')
            || alias.Contains('%')
            || alias.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        if (alias.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            alias = alias[..^4];
        }

        return alias;
    }

    private static void Add(
        Dictionary<string, AppTarget> aliases,
        string[] names,
        string displayName,
        string? directory,
        string executableName,
        string processName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var path = Path.Combine(directory, executableName);
        if (!AppPathPolicy.TryValidatePath(path, out var normalizedPath, out _))
        {
            return;
        }

        var target = new AppTarget(displayName, normalizedPath, processName);
        foreach (var name in names)
        {
            aliases[name] = target;
        }
    }
}
