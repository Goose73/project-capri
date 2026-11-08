using System.Security.Cryptography;
using System.Text.Json;
using Capri.Core.Abstractions;

namespace Capri.Providers;

/// <summary>
/// Resolves a provider API key from an isolated profile credential and
/// performs the one-time migration away from the legacy shared key when the
/// caller allows it. Migration is best-effort: a failure leaves the legacy
/// credential intact and the caller continues with it for the current request.
/// Secret values are never logged.
/// </summary>
public static class CredentialMigration
{
    /// <summary>
    /// Returns the API key for <paramref name="profileCredentialKey"/>. When
    /// that key is absent and <paramref name="allowLegacyFallback"/> is true,
    /// the legacy shared key is copied to the profile key, verified with
    /// <see cref="ISecureCredentialStore.HasAsync"/>, and then deleted.
    /// </summary>
    /// <remarks>
    /// If any migration step fails, the legacy key remains in place and its
    /// value is returned for the current request only. Callers must pass
    /// <paramref name="allowLegacyFallback"/> as false for every provider
    /// except OpenAI.
    /// </remarks>
    public static async Task<string?> ResolveApiKeyAsync(
        ISecureCredentialStore credentials,
        string profileCredentialKey,
        bool allowLegacyFallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileCredentialKey);

        var profileValue = await credentials
            .GetAsync(profileCredentialKey, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(profileValue))
        {
            return profileValue.Trim();
        }

        if (!allowLegacyFallback)
        {
            return null;
        }

        var legacyValue = await credentials
            .GetAsync(ProviderCredentials.LegacyApiKeyCredentialKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(legacyValue))
        {
            return null;
        }

        var value = legacyValue.Trim();

        try
        {
            await credentials
                .SetAsync(profileCredentialKey, value, cancellationToken)
                .ConfigureAwait(false);

            if (await credentials
                    .HasAsync(profileCredentialKey, cancellationToken)
                    .ConfigureAwait(false))
            {
                await credentials
                    .DeleteAsync(ProviderCredentials.LegacyApiKeyCredentialKey, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsMigrationFailure(exception))
        {
            // Best-effort migration: keep the legacy credential and use it for
            // this request only. No credential values are logged.
        }

        return value;
    }

    private static bool IsMigrationFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or CryptographicException
            or InvalidOperationException
            or JsonException
            or NotSupportedException
            or OperationCanceledException;
    }
}
