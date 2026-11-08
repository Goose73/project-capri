namespace Capri.Cli.Configuration;

/// <summary>
/// URL validation and redaction helpers. Any user information, query string,
/// or fragment is treated as potentially sensitive and is never printed.
/// </summary>
public static class SafeUrl
{
    private const int MaxLength = 2048;

    /// <summary>
    /// Validates a provider base URL. Credentials in the authority, query
    /// strings, fragments, and non-HTTP schemes are rejected.
    /// </summary>
    public static bool TryValidateBaseUrl(
        string? value,
        out string normalized,
        out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A base URL is required.";
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            error = "The base URL is too long.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = "The base URL must be an absolute URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "The base URL must use http or https.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "The base URL must include a host.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "The base URL must not contain embedded credentials.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            error = "The base URL must not contain a query string.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            error = "The base URL must not contain a fragment.";
            return false;
        }

        normalized = trimmed;
        return true;
    }

    /// <summary>
    /// Returns a display-safe form of a URL. If the URL cannot be parsed, a
    /// placeholder is returned instead of echoing potentially sensitive input.
    /// </summary>
    public static string ForDisplay(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "(not set)";
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return "(invalid or hidden URL)";
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri.GetLeftPart(UriPartial.Path);
    }
}
