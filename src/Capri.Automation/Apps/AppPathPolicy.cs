namespace Capri.Automation.Apps;

/// <summary>
/// Structural safety checks for application executable paths. This type performs
/// no file-system access so it can be reused by the alias resolver and tested
/// deterministically.
/// </summary>
public static class AppPathPolicy
{
    private static readonly HashSet<string> ForbiddenExecutableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "at",
        "bitsadmin",
        "certutil",
        "cmd",
        "cscript",
        "forfiles",
        "hh",
        "installutil",
        "jsc",
        "msbuild",
        "msconfig",
        "msdt",
        "mshta",
        "msiexec",
        "powershell",
        "powershell_ise",
        "pwsh",
        "reg",
        "regedit",
        "regsvr32",
        "rundll32",
        "schtasks",
        "vbc",
        "wfc",
        "wmic",
        "wscript",
    };

    /// <summary>
    /// Returns the roots where user-launchable executables may live.
    /// </summary>
    public static IReadOnlyList<string> GetDefaultAllowedRoots()
    {
        var roots = new List<string>();

        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            AddRoot(roots, Path.Combine(localApplicationData, "Programs"));
        }

        return roots;
    }

    /// <summary>
    /// Returns true when the executable file name is a known living-off-the-land binary.
    /// </summary>
    public static bool IsForbiddenExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(path.Trim());
        return !string.IsNullOrEmpty(fileName) && ForbiddenExecutableNames.Contains(fileName);
    }

    /// <summary>
    /// Validates path structure, file type, and allowed roots.
    /// </summary>
    /// <param name="path">Candidate absolute executable path.</param>
    /// <param name="normalizedPath">Full path when validation succeeds.</param>
    /// <param name="reason">Human-readable rejection reason when validation fails.</param>
    /// <returns>True when the path is structurally safe.</returns>
    public static bool TryValidatePath(
        string? path,
        out string? normalizedPath,
        out string? reason)
    {
        normalizedPath = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "An application executable path is required.";
            return false;
        }

        var candidate = path.Trim();

        if (candidate.Any(char.IsControl))
        {
            reason = "The application path contains control characters.";
            return false;
        }

        if (candidate.Contains('%'))
        {
            reason = "Environment-variable paths such as %TEMP% are not permitted.";
            return false;
        }

        if (candidate.StartsWith(@"\\", StringComparison.Ordinal)
            || candidate.StartsWith("//", StringComparison.Ordinal))
        {
            reason = "UNC paths are not permitted.";
            return false;
        }

        if (!Path.IsPathFullyQualified(candidate))
        {
            reason = "The application path must be absolute.";
            return false;
        }

        if (ContainsParentTraversal(candidate))
        {
            reason = "Parent-directory traversal ('..') is not permitted.";
            return false;
        }

        // A colon beyond the drive designator indicates an alternate data stream.
        if (candidate.IndexOf(':', 2) >= 0)
        {
            reason = "Alternate data streams are not permitted.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            reason = "The application path is invalid.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Only .exe application files are permitted.";
            return false;
        }

        if (IsForbiddenExecutable(fullPath))
        {
            reason = "The executable is a restricted system tool.";
            return false;
        }

        if (IsUnderRoot(fullPath, Path.GetTempPath()))
        {
            reason = "Applications may not be launched from temporary directories.";
            return false;
        }

        if (!IsUnderAnyRoot(fullPath, GetDefaultAllowedRoots()))
        {
            reason = "The executable is outside the permitted application directories.";
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    private static void AddRoot(List<string> roots, string? root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            roots.Add(root);
        }
    }

    private static bool ContainsParentTraversal(string path)
    {
        var segments = path.Split(['\\', '/'], StringSplitOptions.None);
        return segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static bool IsUnderAnyRoot(string fullPath, IReadOnlyList<string> roots)
    {
        return roots.Any(root => IsUnderRoot(fullPath, root));
    }

    private static bool IsUnderRoot(string fullPath, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
