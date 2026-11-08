using Capri.Cli.Configuration;
using Capri.Cli.Services;
using Capri.Core.Models;
using Capri.Core.Providers;
using Capri.Providers;

namespace Capri.Cli.Commands;

/// <summary>Severity of a doctor finding.</summary>
public enum DoctorSeverity
{
    /// <summary>The check passed.</summary>
    Ok,

    /// <summary>Informational finding; no action required.</summary>
    Info,

    /// <summary>A non-critical issue that may affect usability.</summary>
    Warning,

    /// <summary>A critical issue that must be fixed.</summary>
    Critical,
}

/// <summary>A single diagnostic finding.</summary>
public sealed record DoctorCheck(string Id, DoctorSeverity Severity, string Summary, string? Fix);

/// <summary>Aggregated doctor findings.</summary>
public sealed record DoctorReport(IReadOnlyList<DoctorCheck> Checks)
{
    /// <summary>Number of passing checks.</summary>
    public int OkCount => Checks.Count(check => check.Severity == DoctorSeverity.Ok);

    /// <summary>Number of informational findings.</summary>
    public int InfoCount => Checks.Count(check => check.Severity == DoctorSeverity.Info);

    /// <summary>Number of warnings.</summary>
    public int WarningCount => Checks.Count(check => check.Severity == DoctorSeverity.Warning);

    /// <summary>Number of critical findings.</summary>
    public int CriticalCount => Checks.Count(check => check.Severity == DoctorSeverity.Critical);
}

/// <summary>
/// Diagnoses settings readability, provider configuration, local endpoint
/// reachability, and DPAPI credential-store availability without exposing any
/// credential data.
/// </summary>
public sealed class DoctorCommand
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1000);

    private readonly ICliConsole _console;
    private readonly ICliServices _services;
    private readonly IEndpointProbe _probe;
    private readonly IDpapiAvailabilityProbe _dpapiProbe;

    /// <summary>Creates the doctor command.</summary>
    public DoctorCommand(
        ICliConsole console,
        ICliServices services,
        IEndpointProbe probe,
        IDpapiAvailabilityProbe dpapiProbe)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _dpapiProbe = dpapiProbe ?? throw new ArgumentNullException(nameof(dpapiProbe));
    }

    /// <summary>
    /// Runs diagnostics. Returns <see cref="CliExitCodes.Unhealthy"/> when a
    /// critical issue is found, otherwise <see cref="CliExitCodes.Success"/>.
    /// </summary>
    public async Task<int> ExecuteAsync(bool json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = _services.Settings.Current;
        var inspection = SettingsFileInspector.Inspect(_services.Paths);
        var profile = ProviderCatalog.TryGetById(settings.Provider.ProviderId, out var knownProfile)
            ? knownProfile
            : null;

        var checks = new List<DoctorCheck>();
        AddSettingsChecks(checks, inspection);
        AddProviderChecks(checks, settings, profile);
        await AddLocalEndpointChecksAsync(checks, settings, profile, cancellationToken).ConfigureAwait(false);
        AddDpapiChecks(checks);
        AddCredentialFileChecks(checks);
        await AddCredentialPresenceChecksAsync(checks, profile, cancellationToken).ConfigureAwait(false);

        var report = new DoctorReport(checks);
        if (json)
        {
            var document = new
            {
                checks = checks.Select(check => new
                {
                    id = check.Id,
                    severity = check.Severity,
                    summary = check.Summary,
                    fix = check.Fix,
                }),
                summary = new
                {
                    ok = report.OkCount,
                    info = report.InfoCount,
                    warnings = report.WarningCount,
                    critical = report.CriticalCount,
                },
            };

            _console.WriteLine(JsonOutput.Serialize(document));
        }
        else
        {
            WritePlainReport(report);
        }

        return report.CriticalCount > 0 ? CliExitCodes.Unhealthy : CliExitCodes.Success;
    }

    private static void AddSettingsChecks(List<DoctorCheck> checks, SettingsFileInspection inspection)
    {
        var (severity, fix) = inspection.State switch
        {
            SettingsFileState.Readable => (DoctorSeverity.Ok, (string?)null),
            SettingsFileState.Missing => (
                DoctorSeverity.Info,
                "Run 'capri config set <setting> <value>' or open desktop Settings to create it."),
            SettingsFileState.Empty => (
                DoctorSeverity.Warning,
                "Set a value with 'capri config set' or replace the file; defaults are currently in use."),
            SettingsFileState.Malformed => (
                DoctorSeverity.Critical,
                "Back up, fix, or remove the settings file, then run 'capri doctor' again."),
            SettingsFileState.Unreadable => (
                DoctorSeverity.Critical,
                "Check file permissions and that Capri is running as the same Windows user."),
            _ => (DoctorSeverity.Critical, "Inspect the settings file manually."),
        };

        checks.Add(new DoctorCheck("settings.readable", severity, inspection.Detail, fix));
    }

    private static void AddProviderChecks(
        List<DoctorCheck> checks,
        AppSettings settings,
        ProviderProfile? profile)
    {
        var provider = settings.Provider;

        checks.Add(profile is null
            ? new DoctorCheck(
                "provider.configured",
                DoctorSeverity.Critical,
                "The configured provider is not recognized.",
                "Run 'capri config set provider.id <ollama|lmstudio|openrouter|openai>'.")
            : new DoctorCheck(
                "provider.configured",
                DoctorSeverity.Ok,
                $"Provider preset is recognized: {profile.DisplayName} ({(profile.IsLocal ? "local" : "cloud")}).",
                null));

        checks.Add(string.IsNullOrWhiteSpace(provider.ChatModel)
            ? new DoctorCheck(
                "provider.model",
                DoctorSeverity.Critical,
                "No chat model is configured.",
                "Run 'capri config set provider.model <model-id>'.")
            : new DoctorCheck(
                "provider.model",
                DoctorSeverity.Ok,
                "A chat model is configured.",
                null));

        if (!SafeUrl.TryValidateBaseUrl(provider.BaseUrl, out _, out var urlError))
        {
            checks.Add(new DoctorCheck(
                "provider.baseUrl",
                DoctorSeverity.Critical,
                "The provider base URL is invalid or contains hidden credentials.",
                string.Concat(urlError, " Run 'capri config set provider.baseUrl <url>'.")));
        }
        else
        {
            var displayUrl = SafeUrl.ForDisplay(provider.BaseUrl);
            var insecureRemote = Uri.TryCreate(displayUrl, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.IsLoopback;

            checks.Add(insecureRemote
                ? new DoctorCheck(
                    "provider.baseUrl",
                    DoctorSeverity.Warning,
                    "The remote provider base URL uses unencrypted HTTP.",
                    "Use an https base URL for remote providers.")
                : new DoctorCheck(
                    "provider.baseUrl",
                    DoctorSeverity.Ok,
                    "The provider base URL is valid and contains no embedded credentials.",
                    null));
        }

        if (!double.IsFinite(provider.Temperature) || provider.Temperature is < 0 or > 2)
        {
            checks.Add(new DoctorCheck(
                "provider.requestLimits",
                DoctorSeverity.Warning,
                "Sampling temperature is outside the supported range.",
                "Run 'capri config set provider.temperature <0-2>'."));
        }
        else if (provider.MaxOutputTokens < 1 || provider.RequestTimeoutSeconds < 1)
        {
            checks.Add(new DoctorCheck(
                "provider.requestLimits",
                DoctorSeverity.Warning,
                "A request limit is not positive.",
                "Set provider.maxOutputTokens and provider.timeoutSeconds to positive values."));
        }
        else
        {
            checks.Add(new DoctorCheck(
                "provider.requestLimits",
                DoctorSeverity.Ok,
                "Request limits are within supported ranges.",
                null));
        }

        if (profile is null)
        {
            return;
        }

        if (profile.IsLocal)
        {
            checks.Add(new DoctorCheck(
                "provider.localKeyPolicy",
                DoctorSeverity.Ok,
                "The selected provider is local and requires no API key.",
                null));
            return;
        }

        checks.Add(provider.AllowCloudInference
            ? new DoctorCheck(
                "provider.cloudConsent",
                provider.CloudConsentRecordedUtc is null
                    ? DoctorSeverity.Warning
                    : DoctorSeverity.Ok,
                provider.CloudConsentRecordedUtc is null
                    ? "Cloud inference is enabled but no consent timestamp is recorded."
                    : "Cloud inference is enabled with recorded consent.",
                provider.CloudConsentRecordedUtc is null
                    ? "Enable cloud inference again from desktop Settings or 'capri config set provider.allowCloud true'."
                    : null)
            : new DoctorCheck(
                "provider.cloudConsent",
                DoctorSeverity.Warning,
                "Cloud inference is disabled for the selected cloud provider.",
                "Enable it in desktop Settings or with 'capri config set provider.allowCloud true' (may incur charges)."));

        if (!profile.SupportsFreeModelsOnly && provider.FreeModelsOnly)
        {
            checks.Add(new DoctorCheck(
                "provider.freeModelsOnly",
                DoctorSeverity.Warning,
                "Free-models-only is on, but this cloud provider does not offer free models.",
                "Turn off free-models-only only if you accept possible charges."));
        }
    }

    private async Task AddLocalEndpointChecksAsync(
        List<DoctorCheck> checks,
        AppSettings settings,
        ProviderProfile? profile,
        CancellationToken cancellationToken)
    {
        var activeBaseUrl = profile is { IsLocal: true } ? settings.Provider.BaseUrl : null;
        foreach (var target in LocalEndpoints.GetTargets(settings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displayUrl = SafeUrl.ForDisplay(target.BaseUrl);
            var isActive = activeBaseUrl is not null && SameUrl(activeBaseUrl, target.BaseUrl);

            if (!Uri.TryCreate(displayUrl, UriKind.Absolute, out var probeUri))
            {
                checks.Add(new DoctorCheck(
                    $"endpoint.{Slug(target.Name)}",
                    isActive ? DoctorSeverity.Warning : DoctorSeverity.Info,
                    $"{target.Name} endpoint URL is invalid.",
                    target.FixHint));
                continue;
            }

            var result = await _probe
                .ProbeAsync(probeUri, ProbeTimeout, cancellationToken)
                .ConfigureAwait(false);
            var latency = (int)Math.Round(result.Elapsed.TotalMilliseconds);

            checks.Add(result.Reachable
                ? new DoctorCheck(
                    $"endpoint.{Slug(target.Name)}",
                    DoctorSeverity.Ok,
                    $"{target.Name} is reachable at {displayUrl} ({result.Detail}, {latency} ms).",
                    null)
                : new DoctorCheck(
                    $"endpoint.{Slug(target.Name)}",
                    isActive ? DoctorSeverity.Warning : DoctorSeverity.Info,
                    $"{target.Name} is not reachable at {displayUrl} ({result.Detail}).",
                    target.FixHint));
        }
    }

    private void AddDpapiChecks(List<DoctorCheck> checks)
    {
        var availability = _dpapiProbe.Check();
        checks.Add(availability.Available
            ? new DoctorCheck(
                "credentials.dpapi",
                DoctorSeverity.Ok,
                availability.Detail,
                null)
            : new DoctorCheck(
                "credentials.dpapi",
                DoctorSeverity.Critical,
                availability.Detail,
                "Run Capri as the same Windows user and verify DPAPI is available on this machine."));
    }

    private void AddCredentialFileChecks(List<DoctorCheck> checks)
    {
        var secretsFile = _services.Paths.SecretsFile;
        if (!File.Exists(secretsFile))
        {
            checks.Add(new DoctorCheck(
                "credentials.file",
                DoctorSeverity.Info,
                "No credential file exists yet; no API keys are stored.",
                "Add cloud API keys from the desktop Settings window when needed."));
            return;
        }

        try
        {
            var length = new FileInfo(secretsFile).Length;
            if (length == 0)
            {
                checks.Add(new DoctorCheck(
                    "credentials.file",
                    DoctorSeverity.Warning,
                    "The credential file is empty and is treated as having no credentials.",
                    "Save the key again from desktop Settings or remove the empty credential file."));
                return;
            }

            using var stream = File.OpenRead(secretsFile);
            checks.Add(new DoctorCheck(
                "credentials.file",
                DoctorSeverity.Ok,
                "The credential file is readable (contents were not inspected or displayed).",
                null));
        }
        catch (UnauthorizedAccessException)
        {
            checks.Add(new DoctorCheck(
                "credentials.file",
                DoctorSeverity.Critical,
                "The credential file cannot be read because access was denied.",
                "Check file permissions and that Capri is running as the same Windows user."));
        }
        catch (IOException)
        {
            checks.Add(new DoctorCheck(
                "credentials.file",
                DoctorSeverity.Critical,
                "The credential file cannot be read because of an I/O error.",
                "Check file permissions and that no other process has locked the file."));
        }
    }

    private async Task AddCredentialPresenceChecksAsync(
        List<DoctorCheck> checks,
        ProviderProfile? profile,
        CancellationToken cancellationToken)
    {
        if (profile is null || !profile.RequiresApiKey)
        {
            return;
        }

        try
        {
            var key = ProviderCredentials.GetApiKeyCredentialKey(profile);
            var present = await _services.Credentials
                .HasAsync(key, cancellationToken)
                .ConfigureAwait(false);

            checks.Add(present
                ? new DoctorCheck(
                    "credentials.providerKey",
                    DoctorSeverity.Ok,
                    $"An API key is stored for {profile.DisplayName} (value not shown).",
                    null)
                : new DoctorCheck(
                    "credentials.providerKey",
                    DoctorSeverity.Warning,
                    $"No API key is stored for {profile.DisplayName}.",
                    "Open desktop Settings -> AI providers to add the key. The CLI never accepts secrets."));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            checks.Add(new DoctorCheck(
                "credentials.providerKey",
                DoctorSeverity.Critical,
                "The credential store could not be queried.",
                "Check permissions and that Capri is running as the same Windows user."));
        }
    }

    private void WritePlainReport(DoctorReport report)
    {
        _console.WriteLine("Project Capri doctor");
        _console.WriteLine("--------------------");

        foreach (var check in report.Checks)
        {
            _console.WriteLine($"[{SeverityLabel(check.Severity)}] {check.Id} - {check.Summary}");
            if (!string.IsNullOrWhiteSpace(check.Fix))
            {
                _console.WriteLine($"       Fix: {check.Fix}");
            }
        }

        _console.WriteLine(string.Empty);
        _console.WriteLine(
            $"Summary: {report.OkCount} ok, {report.InfoCount} info, {report.WarningCount} warning(s), {report.CriticalCount} critical.");
        if (report.CriticalCount > 0)
        {
            _console.WriteLine("Critical issues found. Fix them before expecting reliable provider behavior.");
        }
    }

    private static string SeverityLabel(DoctorSeverity severity)
    {
        return severity switch
        {
            DoctorSeverity.Ok => "OK",
            DoctorSeverity.Info => "INFO",
            DoctorSeverity.Warning => "WARN",
            DoctorSeverity.Critical => "CRIT",
            _ => "?",
        };
    }

    private static string Slug(string value)
    {
        var characters = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        return new string(characters).Trim('-');
    }

    private static bool SameUrl(string left, string right)
    {
        return string.Equals(
            left.Trim().TrimEnd('/'),
            right.Trim().TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
