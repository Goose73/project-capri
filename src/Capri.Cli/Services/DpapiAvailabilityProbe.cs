using System.Security.Cryptography;
using System.Text;

namespace Capri.Cli.Services;

/// <summary>Result of a DPAPI availability check.</summary>
public sealed record DpapiAvailability(bool Available, string Detail);

/// <summary>
/// Checks whether Windows DPAPI can protect and unprotect data for the current
/// user. The probe payload is a fixed non-secret string.
/// </summary>
public interface IDpapiAvailabilityProbe
{
    /// <summary>Runs the check.</summary>
    DpapiAvailability Check();
}

/// <summary>Default DPAPI probe using <see cref="DataProtectionScope.CurrentUser"/>.</summary>
public sealed class DpapiAvailabilityProbe : IDpapiAvailabilityProbe
{
    private static readonly byte[] ProbePayload = Encoding.UTF8.GetBytes("capri-dpapi-probe");
    private static readonly byte[] ProbeEntropy = Encoding.UTF8.GetBytes("Capri.Cli.dpapi-probe");

    /// <inheritdoc />
    public DpapiAvailability Check()
    {
        try
        {
            var protectedBytes = ProtectedData.Protect(
                ProbePayload,
                ProbeEntropy,
                DataProtectionScope.CurrentUser);
            var roundTripped = ProtectedData.Unprotect(
                protectedBytes,
                ProbeEntropy,
                DataProtectionScope.CurrentUser);

            return roundTripped.AsSpan().SequenceEqual(ProbePayload)
                ? new DpapiAvailability(true, "DPAPI can protect and unprotect data for the current user.")
                : new DpapiAvailability(false, "DPAPI round-trip returned unexpected data.");
        }
        catch (CryptographicException)
        {
            return new DpapiAvailability(false, "DPAPI is not available for the current user.");
        }
        catch (PlatformNotSupportedException)
        {
            return new DpapiAvailability(false, "DPAPI is not supported on this platform.");
        }
    }
}
