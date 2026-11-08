using Capri.Cli.Configuration;
using Capri.Cli.Services;
using Capri.Core.Models;
using Capri.Core.Providers;
using Capri.Providers;

namespace Capri.Cli.Commands;

/// <summary>
/// Shows effective settings, configuration path state, credential presence for
/// the active profile, and best-effort local endpoint reachability.
/// </summary>
public sealed class StatusCommand
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1000);

    private readonly ICliConsole _console;
    private readonly ICliServices _services;
    private readonly IEndpointProbe _probe;

    /// <summary>Creates the status command.</summary>
    public StatusCommand(ICliConsole console, ICliServices services, IEndpointProbe probe)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <summary>Runs the command. Status is informational, so it returns 0 unless cancelled.</summary>
    public async Task<int> ExecuteAsync(bool json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = _services.Settings.Current;
        var provider = settings.Provider;
        var inspection = SettingsFileInspector.Inspect(_services.Paths);
        var profile = ProviderCatalog.TryGetById(provider.ProviderId, out var knownProfile)
            ? knownProfile
            : null;

        var credential = await GetCredentialStatusAsync(profile, cancellationToken).ConfigureAwait(false);
        var endpoints = await ProbeLocalEndpointsAsync(settings, cancellationToken)
            .ConfigureAwait(false);

        if (json)
        {
            var document = new
            {
                settingsFile = new
                {
                    path = _services.Paths.SettingsFile,
                    exists = inspection.State != SettingsFileState.Missing,
                    state = StateName(inspection.State),
                    detail = inspection.Detail,
                },
                provider = new
                {
                    id = provider.ProviderId,
                    displayName = profile?.DisplayName ?? "Unknown",
                    isLocal = profile?.IsLocal ?? false,
                    requiresApiKey = profile?.RequiresApiKey ?? false,
                    model = provider.ChatModel,
                    baseUrl = SafeUrl.ForDisplay(provider.BaseUrl),
                    streaming = provider.EnableStreaming,
                    freeModelsOnly = provider.FreeModelsOnly,
                    allowCloudInference = provider.AllowCloudInference,
                    cloudConsentRecorded = provider.CloudConsentRecordedUtc is not null,
                },
                credential = new
                {
                    status = credential.Status,
                    detail = credential.Detail,
                },
                localEndpoints = endpoints.Select(endpoint => new
                {
                    name = endpoint.Name,
                    url = endpoint.Url,
                    reachable = endpoint.Reachable,
                    detail = endpoint.Detail,
                    latencyMs = endpoint.LatencyMilliseconds,
                }),
            };

            _console.WriteLine(JsonOutput.Serialize(document));
            return CliExitCodes.Success;
        }

        _console.WriteLine("Project Capri status");
        _console.WriteLine("---------------------");
        WriteField(
            "Settings file",
            $"{_services.Paths.SettingsFile} ({SettingsStateLabel(inspection.State)})");
        WriteField(
            "Provider",
            profile is null
                ? $"{provider.ProviderId} (unknown provider)"
                : $"{profile.Id} ({profile.DisplayName}, {(profile.IsLocal ? "local" : "cloud")}, {(profile.RequiresApiKey ? "API key required" : "no API key")})");
        WriteField("Model", string.IsNullOrWhiteSpace(provider.ChatModel) ? "(not set)" : provider.ChatModel);
        WriteField("Base URL", SafeUrl.ForDisplay(provider.BaseUrl));
        WriteField("Streaming", OnOff(provider.EnableStreaming));
        WriteField("Free models only", OnOff(provider.FreeModelsOnly));
        WriteField("Cloud allowed", provider.AllowCloudInference ? "yes (consent recorded)" : "no");
        WriteField("API key", credential.Detail);
        _console.WriteLine("Local endpoints:");

        foreach (var endpoint in endpoints)
        {
            var state = endpoint.Reachable ? "reachable" : "not reachable";
            var latency = endpoint.Reachable
                ? $", {endpoint.LatencyMilliseconds} ms"
                : string.Empty;
            _console.WriteLine($"  {endpoint.Name,-24} {state} ({endpoint.Detail}{latency})");
        }

        return CliExitCodes.Success;
    }

    private async Task<CredentialStatus> GetCredentialStatusAsync(
        ProviderProfile? profile,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return new CredentialStatus("unknown-provider", "unknown provider; no credential lookup performed");
        }

        if (!profile.RequiresApiKey)
        {
            return new CredentialStatus(
                "not-required",
                profile.IsLocal ? "not required (local provider)" : "not required");
        }

        try
        {
            var key = ProviderCredentials.GetApiKeyCredentialKey(profile);
            var present = await _services.Credentials
                .HasAsync(key, cancellationToken)
                .ConfigureAwait(false);

            return present
                ? new CredentialStatus("present", $"present for {profile.DisplayName} (value not shown)")
                : new CredentialStatus("missing", $"missing for {profile.DisplayName}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new CredentialStatus("unavailable", "credential store unavailable");
        }
    }

    private async Task<IReadOnlyList<EndpointStatus>> ProbeLocalEndpointsAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var targets = LocalEndpoints.GetTargets(settings);
        var probes = targets.Select(target => ProbeTargetAsync(target, cancellationToken));
        return await Task.WhenAll(probes).ConfigureAwait(false);
    }

    private async Task<EndpointStatus> ProbeTargetAsync(
        LocalEndpointTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var displayUrl = SafeUrl.ForDisplay(target.BaseUrl);

        if (!Uri.TryCreate(displayUrl, UriKind.Absolute, out var probeUri))
        {
            return new EndpointStatus(target.Name, displayUrl, false, "invalid base URL", 0);
        }

        var result = await _probe
            .ProbeAsync(probeUri, ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);

        return new EndpointStatus(
            target.Name,
            displayUrl,
            result.Reachable,
            result.Detail,
            (int)Math.Round(result.Elapsed.TotalMilliseconds));
    }

    private void WriteField(string label, string value)
    {
        _console.WriteLine($"{label,-20}{value}");
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string StateName(SettingsFileState state)
    {
        return state switch
        {
            SettingsFileState.Missing => "missing",
            SettingsFileState.Empty => "empty",
            SettingsFileState.Readable => "readable",
            SettingsFileState.Malformed => "malformed",
            SettingsFileState.Unreadable => "unreadable",
            _ => "unknown",
        };
    }

    private static string SettingsStateLabel(SettingsFileState state)
    {
        return state switch
        {
            SettingsFileState.Missing => "not created; defaults in use",
            SettingsFileState.Empty => "empty; defaults in use",
            SettingsFileState.Readable => "exists and is readable",
            SettingsFileState.Malformed => "exists but is malformed",
            SettingsFileState.Unreadable => "exists but is unreadable",
            _ => "state unknown",
        };
    }

    private sealed record CredentialStatus(string Status, string Detail);

    private sealed record EndpointStatus(
        string Name,
        string Url,
        bool Reachable,
        string Detail,
        int LatencyMilliseconds);
}
