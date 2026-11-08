using System.ComponentModel;
using System.Diagnostics;

namespace Capri.Cli.Services;

/// <summary>How the desktop app should be launched.</summary>
public enum DesktopLaunchKind
{
    /// <summary>A built executable was found.</summary>
    BuiltExecutable,

    /// <summary>Fall back to <c>dotnet run</c> for the desktop project.</summary>
    DotNetRun,

    /// <summary>No repository or executable could be found.</summary>
    Unavailable,
}

/// <summary>A fully resolved launch instruction.</summary>
public sealed record DesktopLaunchPlan(
    DesktopLaunchKind Kind,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string Description);

/// <summary>Result of attempting to launch the desktop application.</summary>
public sealed record DesktopLaunchResult(bool Started, string Message);

/// <summary>Launches (or hands off to) the desktop application.</summary>
public interface IDesktopLauncher
{
    /// <summary>Starts the desktop app without waiting for it to exit.</summary>
    DesktopLaunchResult Launch();
}

/// <summary>
/// Pure path discovery for the desktop app. Mirrors the built-executable-first
/// behavior of capri.cmd and never duplicates desktop AI logic.
/// </summary>
public static class DesktopLaunchPlanner
{
    private static readonly string[] ExecutableCandidates =
    [
        @"src\Capri.Desktop\bin\Debug\net10.0-windows\Capri.Desktop.exe",
        @"src\Capri.Desktop\bin\Release\net10.0-windows\Capri.Desktop.exe",
        @"src\Capri.Desktop\bin\Debug\net10.0-windows\win-x64\Capri.Desktop.exe",
        @"src\Capri.Desktop\bin\Release\net10.0-windows\win-x64\Capri.Desktop.exe",
        @"src\Capri.Desktop\bin\Debug\net10.0-windows\publish\Capri.Desktop.exe",
        @"src\Capri.Desktop\bin\Release\net10.0-windows\publish\Capri.Desktop.exe",
    ];

    private const string DesktopProjectRelativePath =
        @"src\Capri.Desktop\Capri.Desktop.csproj";

    /// <summary>Creates a launch plan for the supplied repository root.</summary>
    public static DesktopLaunchPlan Create(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return Unavailable("The Project Capri repository root could not be located.");
        }

        string root;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot.Trim()));
        }
        catch (ArgumentException)
        {
            return Unavailable("The Project Capri repository path is not valid.");
        }
        catch (NotSupportedException)
        {
            return Unavailable("The Project Capri repository path is not valid.");
        }

        foreach (var relativePath in ExecutableCandidates)
        {
            var executable = Path.Combine(root, relativePath);
            if (!File.Exists(executable))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(executable) ?? root;
            return new DesktopLaunchPlan(
                DesktopLaunchKind.BuiltExecutable,
                executable,
                Array.Empty<string>(),
                directory,
                "Launching the built desktop app.");
        }

        var project = Path.Combine(root, DesktopProjectRelativePath);
        if (File.Exists(project))
        {
            return new DesktopLaunchPlan(
                DesktopLaunchKind.DotNetRun,
                "dotnet",
                ["run", "--project", project, "-c", "Debug"],
                root,
                "Built executable not found; building and running via dotnet run.");
        }

        return Unavailable(
            "No desktop executable or project was found. Build Capri.sln and try again.");
    }

    private static DesktopLaunchPlan Unavailable(string message)
    {
        return new DesktopLaunchPlan(
            DesktopLaunchKind.Unavailable,
            string.Empty,
            Array.Empty<string>(),
            string.Empty,
            message);
    }
}

/// <summary>Default desktop launcher.</summary>
public sealed class DesktopLauncher : IDesktopLauncher
{
    private readonly Func<string?> _repositoryRootProvider;

    /// <summary>Creates a launcher with an explicit repository root provider.</summary>
    public DesktopLauncher(Func<string?> repositoryRootProvider)
    {
        _repositoryRootProvider = repositoryRootProvider
            ?? throw new ArgumentNullException(nameof(repositoryRootProvider));
    }

    /// <summary>Creates a launcher that discovers the repository at launch time.</summary>
    public static DesktopLauncher CreateDefault()
    {
        return new DesktopLauncher(RepositoryLocator.FindRepositoryRoot);
    }

    /// <inheritdoc />
    public DesktopLaunchResult Launch()
    {
        var plan = DesktopLaunchPlanner.Create(_repositoryRootProvider());
        if (plan.Kind == DesktopLaunchKind.Unavailable)
        {
            return new DesktopLaunchResult(false, plan.Description);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in plan.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            return process is null
                ? new DesktopLaunchResult(false, "The desktop process could not be started.")
                : new DesktopLaunchResult(true, plan.Description);
        }
        catch (Win32Exception)
        {
            return new DesktopLaunchResult(
                false,
                "The desktop process could not be started. Build Capri.sln and try again.");
        }
        catch (InvalidOperationException)
        {
            return new DesktopLaunchResult(
                false,
                "The desktop process could not be started. Build Capri.sln and try again.");
        }
    }
}

/// <summary>Locates the repository root without relying on the current directory alone.</summary>
public static class RepositoryLocator
{
    /// <summary>Environment variable that pins the repository root.</summary>
    public const string RepositoryRootEnvironmentVariable = "CAPRI_REPO_ROOT";

    private const int MaxParentLevels = 12;

    /// <summary>
    /// Checks <c>CAPRI_REPO_ROOT</c>, then walks up from the current directory
    /// and the application base directory looking for <c>Capri.sln</c>.
    /// </summary>
    public static string? FindRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable(RepositoryRootEnvironmentVariable);
        if (IsRepositoryRoot(configured))
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured!.Trim()));
        }

        return FindFrom(Directory.GetCurrentDirectory())
            ?? FindFrom(AppContext.BaseDirectory);
    }

    private static string? FindFrom(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        for (var level = 0; directory is not null && level < MaxParentLevels; level++)
        {
            if (IsRepositoryRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsRepositoryRoot(string? directory)
    {
        return !string.IsNullOrWhiteSpace(directory)
            && File.Exists(Path.Combine(directory.Trim(), "Capri.sln"));
    }
}
