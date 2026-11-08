using System.Diagnostics.CodeAnalysis;

namespace Capri.Automation.Web;

/// <summary>
/// Conservative policy for web addresses supplied by a user or language model.
/// Only absolute <c>http</c> and <c>https</c> URIs are accepted. Bare host names
/// are intentionally rejected rather than guessing a scheme.
/// </summary>
public static class UrlPolicy
{
    private static readonly string[] DangerousSchemes =
    [
        "file",
        "javascript",
        "data",
        "vbscript",
        "shell",
        "search-ms",
    ];

    /// <summary>
    /// Validates <paramref name="candidate"/> and returns a detailed outcome.
    /// </summary>
    public static UrlValidationResult Validate(string? candidate)
    {
        if (!TryValidate(candidate, out var uri, out var reason))
        {
            return UrlValidationResult.Failure(reason!);
        }

        return UrlValidationResult.Success(uri!);
    }

    /// <summary>
    /// Validates <paramref name="candidate"/> and exposes the parsed URI and
    /// rejection reason.
    /// </summary>
    public static bool TryValidate(
        string? candidate,
        [NotNullWhen(true)] out Uri? uri,
        [NotNullWhen(false)] out string? reason)
    {
        uri = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            reason = "A web address is required.";
            return false;
        }

        var input = candidate.Trim();

        if (input.Any(char.IsControl))
        {
            reason = "The web address contains control characters.";
            return false;
        }

        if (input.Any(char.IsWhiteSpace))
        {
            reason = "The web address must not contain spaces; use percent-encoding instead.";
            return false;
        }

        if (input.Contains('\\'))
        {
            reason = "Backslashes are not permitted in web addresses.";
            return false;
        }

        if (input.StartsWith("//", StringComparison.Ordinal))
        {
            reason = "Protocol-relative and UNC-style web addresses are not permitted.";
            return false;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var parsed))
        {
            reason = "The web address must be an absolute http or https address.";
            return false;
        }

        var scheme = parsed.Scheme.ToLowerInvariant();

        if (DangerousSchemes.Contains(scheme, StringComparer.Ordinal)
            || scheme.StartsWith("ms-", StringComparison.Ordinal))
        {
            reason = $"The '{parsed.Scheme}' scheme is not permitted.";
            return false;
        }

        if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            reason = $"Only http and https web addresses are permitted; '{parsed.Scheme}' is not supported.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo) || AuthorityContainsUserInfoMarker(input))
        {
            reason = "Web addresses containing user credentials are not permitted.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.Host))
        {
            reason = "The web address must include a host name.";
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// Detects an '@' in the authority even when the runtime normalizes an empty
    /// user-info component away from <see cref="Uri.UserInfo"/>.
    /// </summary>
    private static bool AuthorityContainsUserInfoMarker(string input)
    {
        var schemeSeparator = input.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            return false;
        }

        var authorityStart = schemeSeparator + 3;
        var authorityEnd = input.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0
            ? input[authorityStart..]
            : input[authorityStart..authorityEnd];

        return authority.Contains('@');
    }
}

/// <summary>
/// Outcome of <see cref="UrlPolicy"/> validation.
/// </summary>
/// <param name="IsValid">True when the URI is safe to open.</param>
/// <param name="Uri">Parsed URI when valid.</param>
/// <param name="Reason">Human-readable rejection reason when invalid.</param>
public sealed record UrlValidationResult(bool IsValid, Uri? Uri, string? Reason)
{
    internal static UrlValidationResult Success(Uri uri) => new(true, uri, null);

    internal static UrlValidationResult Failure(string reason) => new(false, null, reason);
}
