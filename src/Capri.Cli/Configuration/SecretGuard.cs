namespace Capri.Cli.Configuration;

/// <summary>
/// Rejects setting names that could refer to credentials. The CLI never reads
/// or writes secrets, so these names are blocked before any lookup occurs.
/// </summary>
public static class SecretGuard
{
    private static readonly string[] SensitiveSuffixes =
    [
        "secret",
        "apikey",
        "token",
        "password",
        "passwd",
        "credential",
        "credentials",
        "bearer",
        "authorization",
    ];

    /// <summary>
    /// True when the setting name looks like a credential or secret. The value
    /// itself is never inspected or echoed.
    /// </summary>
    public static bool IsSecretLike(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return false;
        }

        // Compare against the compact alphanumeric form so apiKey, api_key,
        // and provider.openai.apiKey are all recognized while safe names such
        // as provider.maxOutputTokens are not mistaken for auth tokens.
        var compact = new string(
            setting.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        foreach (var suffix in SensitiveSuffixes)
        {
            if (compact.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
