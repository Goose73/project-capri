namespace Capri.Cli;

/// <summary>
/// Safe, generic error descriptions. Exception messages are intentionally not
/// echoed because they can contain paths, URLs, or other unintended details.
/// </summary>
public static class CliErrors
{
    /// <summary>Returns a safe category description for an unexpected exception.</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            UnauthorizedAccessException => "access denied while reading or writing Capri data.",
            IOException => "an I/O error occurred while accessing Capri data.",
            HttpRequestException => "a network request failed.",
            InvalidOperationException => "the requested operation could not be completed.",
            NotSupportedException => "the requested operation is not supported on this system.",
            _ => $"unexpected error ({exception.GetType().Name}).",
        };
    }
}
