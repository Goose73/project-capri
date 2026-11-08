namespace Capri.Cli;

/// <summary>
/// Process exit codes used by every CLI command. Values are stable so scripts
/// can branch on them.
/// </summary>
public static class CliExitCodes
{
    /// <summary>The command completed successfully.</summary>
    public const int Success = 0;

    /// <summary>An unexpected runtime, I/O, or launch error occurred.</summary>
    public const int Error = 1;

    /// <summary>The command line was unknown, incomplete, or invalid.</summary>
    public const int Usage = 2;

    /// <summary>Doctor found one or more critical issues.</summary>
    public const int Unhealthy = 3;

    /// <summary>The user cancelled the command (Ctrl+C).</summary>
    public const int Cancelled = 130;
}
