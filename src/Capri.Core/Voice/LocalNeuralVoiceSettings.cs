using System.Net;

namespace Capri.Core.Voice;

/// <summary>
/// Accept only literal loopback HTTP endpoints for optional local neural TTS.
/// Never turn an arbitrary user-configured URL into a speech upload endpoint.
/// </summary>
public static class LocalNeuralVoiceSettings
{
    public const string DefaultEndpoint = "http://127.0.0.1:5000";

    public static bool TryGetSynthesisUri(string? address, out Uri? synthesis)
    {
        synthesis = null;
        if (string.IsNullOrWhiteSpace(address) ||
            address.Length > 180 ||
            !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath is not "/" ||
            !IPAddress.TryParse(uri.Host, out var ip) ||
            !IPAddress.IsLoopback(ip) ||
            uri.Port is < 1024 or > 65535)
            return false;

        synthesis = new UriBuilder(uri) { Path = "/synthesize" }.Uri;
        return true;
    }
}
