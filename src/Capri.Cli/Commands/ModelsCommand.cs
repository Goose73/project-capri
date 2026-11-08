using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Capri.Cli.Configuration;
using Capri.Cli.Services;
using Capri.Core.Models;
using Capri.Core.Providers;
using Capri.Security;

namespace Capri.Cli.Commands;

/// <summary>
/// Lists, recommends, downloads (with explicit confirmation), and activates
/// local Ollama models.
/// </summary>
public sealed class ModelsCommand
{
    private readonly ICliConsole _console;
    private readonly ICliServices _services;
    private readonly IOllamaClient _ollama;

    /// <summary>Creates the command.</summary>
    public ModelsCommand(ICliConsole console, ICliServices services, IOllamaClient ollama)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
    }

    /// <summary>Executes a models subcommand.</summary>
    public async Task<int> ExecuteAsync(CliInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (invocation.Arguments.Count == 0)
        {
            return Usage("Usage: capri models <list|recommend|pull|use> [args].");
        }

        var subcommand = invocation.Arguments[0];
        return subcommand switch
        {
            "list" => await ListAsync(invocation.Json, cancellationToken).ConfigureAwait(false),
            "recommend" => await RecommendAsync(invocation.Json, cancellationToken).ConfigureAwait(false),
            "pull" => await PullAsync(GetModelArg(invocation), invocation.Yes, invocation.Json, cancellationToken).ConfigureAwait(false),
            "use" => await UseAsync(GetModelArg(invocation), invocation.Json, cancellationToken).ConfigureAwait(false),
            _ => Usage("Unknown models command. Use list, recommend, pull, or use."),
        };
    }

    private static string GetModelArg(CliInvocation invocation)
    {
        return invocation.Arguments.Count > 1 ? invocation.Arguments[1] : string.Empty;
    }

    private string GetOllamaBaseUrl()
    {
        var provider = _services.Settings.Current.Provider;
        if (ProviderCatalog.TryGetById(provider.ProviderId, out var profile)
            && string.Equals(profile.Id, ProviderCatalog.OllamaId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            return provider.BaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(provider.BaseUrl)
            && Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri)
            && uri.IsLoopback
            && (uri.Port == 11434 || uri.Port == -1))
        {
            return provider.BaseUrl;
        }

        return ProviderCatalog.Ollama.DefaultBaseUrl;
    }

    private async Task<int> ListAsync(bool json, CancellationToken cancellationToken)
    {
        IReadOnlyList<OllamaModelInfo> models;
        try
        {
            models = await _ollama.ListModelsAsync(GetOllamaBaseUrl(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsLocalApiFailure(exception))
        {
            return ReportLocalApiFailure("list installed models", exception, json);
        }

        if (json)
        {
            var payload = models.Select(model => new
            {
                name = model.Name,
                sizeBytes = model.Size,
                size = FormatBytes(model.Size),
                modifiedAt = model.ModifiedAt,
                details = model.Details is null ? null : new
                {
                    family = model.Details.Family,
                    parameterSize = model.Details.ParameterSize,
                    quantization = model.Details.QuantizationLevel,
                    format = model.Details.Format,
                },
            });
            _console.WriteLine(JsonOutput.Serialize(new { models = payload }));
            return CliExitCodes.Success;
        }

        _console.WriteLine($"Installed Ollama models ({OllamaClient.GetApiRoot(GetOllamaBaseUrl())}/api/tags):");
        if (models.Count == 0)
        {
            _console.WriteLine("  (none found)");
            _console.WriteLine("  Use `capri models recommend` and `capri models pull <model>`.");
            return CliExitCodes.Success;
        }

        foreach (var model in models.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            var details = new List<string>();
            if (model.Size.HasValue)
            {
                details.Add(FormatBytes(model.Size));
            }

            if (!string.IsNullOrWhiteSpace(model.Details?.ParameterSize))
            {
                details.Add(model.Details!.ParameterSize!);
            }

            if (!string.IsNullOrWhiteSpace(model.Details?.QuantizationLevel))
            {
                details.Add(model.Details!.QuantizationLevel!);
            }

            var suffix = details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty;
            _console.WriteLine($"  - {model.Name}{suffix}");
            if (!string.IsNullOrWhiteSpace(model.ModifiedAt))
            {
                _console.WriteLine($"    modified: {model.ModifiedAt}");
            }
        }

        return CliExitCodes.Success;
    }

    private async Task<int> RecommendAsync(bool json, CancellationToken cancellationToken)
    {
        var hardware = HardwareInfo.Detect();
        var ollamaBaseUrl = GetOllamaBaseUrl();
        IReadOnlyList<OllamaModelInfo> installed = [];
        string? installNote = null;
        try
        {
            installed = await _ollama.ListModelsAsync(ollamaBaseUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsLocalApiFailure(exception))
        {
            installNote = "Could not query Ollama; installed status unavailable.";
        }

        var installedNames = new HashSet<string>(
            installed.Select(m => m.Name),
            StringComparer.OrdinalIgnoreCase);
        var candidates = GetRecommendedCandidates(hardware)
            .Select(candidate => candidate with { Installed = IsInstalled(candidate.Name, installedNames) })
            .ToList();

        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                hardware = new
                {
                    cpuLogicalCores = hardware.LogicalCores,
                    ramGb = hardware.RamGb,
                    gpuName = hardware.GpuName,
                    vramGbEstimate = hardware.VramGbEstimate,
                    diskFreeGb = hardware.DiskFreeGb,
                    estimates = true,
                    note = hardware.Note,
                },
                ollama = new
                {
                    root = OllamaClient.GetApiRoot(ollamaBaseUrl),
                    installStatusAvailable = installNote is null,
                    note = installNote,
                },
                candidates = candidates.Select(c => new
                {
                    name = c.Name,
                    approxDownloadSize = c.ApproxDownloadSize,
                    contextInitial = c.ContextInitial,
                    capabilities = c.Capabilities,
                    fit = c.Fit,
                    reason = c.Reason,
                    installed = c.Installed,
                }),
            }));
            return CliExitCodes.Success;
        }

        _console.WriteLine("Hardware (best-effort, detected locally; values may be estimates):");
        _console.WriteLine($"  CPU logical cores: {hardware.LogicalCores?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        _console.WriteLine($"  RAM:               {FormatGb(hardware.RamGb)}");
        _console.WriteLine($"  GPU:               {hardware.GpuName ?? "unknown"}");
        _console.WriteLine($"  VRAM (estimate):   {FormatGb(hardware.VramGbEstimate)}");
        _console.WriteLine($"  Disk free:         {FormatGb(hardware.DiskFreeGb)}");
        if (!string.IsNullOrWhiteSpace(hardware.Note))
        {
            _console.WriteLine($"  Note:              {hardware.Note}");
        }

        if (installNote is not null)
        {
            _console.WriteLine($"  Ollama:            {installNote}");
        }

        _console.WriteLine(string.Empty);
        _console.WriteLine("Recommended compact local models (4096 context initial where applicable):");
        foreach (var candidate in candidates)
        {
            var installedMark = candidate.Installed ? " [installed]" : string.Empty;
            _console.WriteLine($"  - {candidate.Name}{installedMark}");
            _console.WriteLine($"    size: {candidate.ApproxDownloadSize}; context: {candidate.ContextInitial}; fit: {candidate.Fit}");
            _console.WriteLine($"    capabilities: {candidate.Capabilities}");
            _console.WriteLine($"    reason: {candidate.Reason}");
        }

        _console.WriteLine(string.Empty);
        _console.WriteLine("Capri never downloads models automatically. Use `capri models pull <model>` and confirm.");
        return CliExitCodes.Success;
    }

    private async Task<int> PullAsync(string modelName, bool yes, bool json, CancellationToken cancellationToken)
    {
        if (!TryValidateModelName(modelName, out var error))
        {
            return json
                ? JsonError("invalid_model", error)
                : Usage(error);
        }

        var normalized = modelName.Trim();
        var knownSize = GetKnownApproxSize(normalized);
        var sizeText = knownSize ?? "size varies; Ollama will report progress";

        if (!_console.IsInteractive && !yes)
        {
            const string message = "Model downloads require explicit confirmation. Re-run with --yes in a non-interactive environment after reviewing the model tag and size.";
            if (json)
            {
                return JsonError("confirmation_required", message, new { model = normalized, approxDownloadSize = sizeText, yesFlag = "--yes" });
            }

            _console.WriteErrorLine($"capri: {message}");
            _console.WriteErrorLine($"  model: {normalized}");
            _console.WriteErrorLine($"  size:  {sizeText} (best-known/estimate where available)");
            _console.WriteErrorLine("  run:   capri models pull " + normalized + " --yes");
            return CliExitCodes.Usage;
        }

        if (!yes)
        {
            _console.WriteLine($"Model: {normalized}");
            _console.WriteLine($"Best-known/estimated size: {sizeText}");
            _console.WriteLine("This will contact the local Ollama API to download the model. Capri does not download silently.");
            _console.Write("Continue with download? [y/N]: ");
            var answer = _console.ReadLine();
            if (!IsYes(answer))
            {
                _console.WriteLine("Cancelled. No model was downloaded.");
                return CliExitCodes.Success;
            }
        }
        else if (!json)
        {
            _console.WriteLine($"Pulling {normalized} ({sizeText}) with explicit --yes confirmation...");
        }

        try
        {
            await foreach (var progress in _ollama.PullModelAsync(GetOllamaBaseUrl(), normalized, cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(progress.Error))
                {
                    if (json)
                    {
                        return JsonError("pull_failed", progress.Error!, new { model = normalized });
                    }

                    _console.WriteErrorLine($"capri: pull failed: {progress.Error}");
                    return CliExitCodes.Error;
                }

                if (json)
                {
                    continue;
                }

                var line = FormatPullProgress(progress);
                if (line is not null)
                {
                    _console.WriteLine(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsLocalApiFailure(exception))
        {
            return ReportLocalApiFailure($"pull model '{normalized}'", exception, json);
        }

        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, model = normalized, status = "success" }));
        }
        else
        {
            _console.WriteLine($"Model pull completed: {normalized}");
        }

        return CliExitCodes.Success;
    }

    private async Task<int> UseAsync(string modelName, bool json, CancellationToken cancellationToken)
    {
        if (!TryValidateModelName(modelName, out var error))
        {
            return json ? JsonError("invalid_model", error) : Usage(error);
        }

        var normalized = modelName.Trim();
        bool exists;
        try
        {
            exists = await _ollama.ModelExistsAsync(GetOllamaBaseUrl(), normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsLocalApiFailure(exception))
        {
            return ReportLocalApiFailure($"verify model '{normalized}'", exception, json);
        }

        if (!exists)
        {
            var message = $"Model '{normalized}' is not installed in the local Ollama instance. Use `capri models list` or `capri models pull {normalized}`.";
            if (json)
            {
                return JsonError("model_not_found", message, new { model = normalized });
            }

            _console.WriteErrorLine($"capri: {message}");
            return CliExitCodes.Error;
        }

        var inspection = SettingsFileInspector.Inspect(_services.Paths);
        if (inspection.State is SettingsFileState.Empty or SettingsFileState.Malformed or SettingsFileState.Unreadable)
        {
            var message = "The settings file cannot be safely updated. Run `capri doctor` for guidance.";
            if (json)
            {
                return JsonError("settings_unwritable", message);
            }

            _console.WriteErrorLine($"capri: {message}");
            return CliExitCodes.Error;
        }

        var current = _services.Settings.Current;
        var provider = current.Provider;
        // If current provider is not a local-capable/ollama-compatible choice, set provider to ollama + default local base URL.
        var profile = ProviderCatalog.TryGetById(provider.ProviderId, out var known) ? known : null;
        var isLocalCapable = profile?.IsLocal == true || ProviderCatalog.IsLoopbackAllowedHttp(provider.BaseUrl);
        var providerId = isLocalCapable && !string.IsNullOrWhiteSpace(provider.ProviderId)
            ? provider.ProviderId
            : ProviderCatalog.OllamaId;
        var baseUrl = isLocalCapable && !string.IsNullOrWhiteSpace(provider.BaseUrl)
            ? provider.BaseUrl
            : ProviderCatalog.Ollama.DefaultBaseUrl;
        if (!SafeUrl.TryValidateBaseUrl(baseUrl, out _, out _))
        {
            providerId = ProviderCatalog.OllamaId;
            baseUrl = ProviderCatalog.Ollama.DefaultBaseUrl;
        }

        var updated = current with
        {
            Provider = provider with
            {
                ProviderId = providerId,
                BaseUrl = baseUrl,
                ChatModel = normalized,
            },
        };

        await _services.Settings.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);

        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                ok = true,
                model = normalized,
                providerId,
                baseUrl = SafeUrl.ForDisplay(baseUrl),
                settingsFile = _services.Paths.SettingsFile,
            }));
            return CliExitCodes.Success;
        }

        _console.WriteLine($"Active model saved: {normalized}");
        _console.WriteLine($"Provider: {providerId}");
        _console.WriteLine($"Base URL: {SafeUrl.ForDisplay(baseUrl)}");
        _console.WriteLine($"Settings: {_services.Paths.SettingsFile}");
        return CliExitCodes.Success;
    }

    private int ReportLocalApiFailure(string action, Exception exception, bool json)
    {
        var root = OllamaClient.GetApiRoot(GetOllamaBaseUrl());
        var detail = exception is HttpRequestException ? "connection failed" : exception.GetType().Name;
        var message = $"Could not {action}. Ensure Ollama is running at {root} (try `ollama serve` or open the Ollama app).";
        if (json)
        {
            return JsonError("ollama_unavailable", message, new { root, detail });
        }

        _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Error;
    }

    private static bool IsLocalApiFailure(Exception exception)
    {
        return exception is HttpRequestException or IOException or TaskCanceledException or JsonException;
    }

    private int Usage(string message)
    {
        _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Usage;
    }

    private int JsonError(string code, string message, object? details = null)
    {
        _console.WriteLine(JsonOutput.Serialize(new { ok = false, error = new { code, message, details } }));
        return code == "confirmation_required" || code == "invalid_model" ? CliExitCodes.Usage : CliExitCodes.Error;
    }

    private static bool TryValidateModelName(string? value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A model name is required.";
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 200)
        {
            error = "Model name is too long.";
            return false;
        }

        if (trimmed.Any(char.IsWhiteSpace) || trimmed.Any(char.IsControl))
        {
            error = "Model name must not contain whitespace or control characters.";
            return false;
        }

        if (trimmed.Contains("..", StringComparison.Ordinal) || trimmed.StartsWith('/') || trimmed.StartsWith('\\'))
        {
            error = "Model name is not valid.";
            return false;
        }

        return true;
    }

    private static bool IsYes(string? value)
    {
        return string.Equals(value?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatPullProgress(OllamaPullProgress progress)
    {
        if (string.Equals(progress.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return "  status: success";
        }

        if (progress.Total is > 0 && progress.Completed.HasValue)
        {
            var pct = Math.Clamp(progress.Completed.Value * 100d / progress.Total.Value, 0d, 100d);
            return $"  {progress.Status}: {pct:0.#}% ({FormatBytes(progress.Completed)}/{FormatBytes(progress.Total)})";
        }

        if (progress.Completed.HasValue)
        {
            return $"  {progress.Status}: {FormatBytes(progress.Completed)}";
        }

        if (!string.IsNullOrWhiteSpace(progress.Status))
        {
            return $"  {progress.Status}";
        }

        return null;
    }

    private static bool IsInstalled(string candidateName, HashSet<string> installed)
    {
        if (installed.Contains(candidateName))
        {
            return true;
        }

        var baseName = candidateName.Split(':')[0];
        return installed.Any(name =>
            string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name.Split(':')[0], baseName, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0)
        {
            return "unknown size";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.Value} {units[unit]}"
            : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    private static string FormatGb(double? gb)
    {
        return gb.HasValue ? $"{gb.Value.ToString("0.#", CultureInfo.InvariantCulture)} GB" : "unknown";
    }

    private static string? GetKnownApproxSize(string modelName)
    {
        return modelName.ToLowerInvariant() switch
        {
            "qwen3.5:4b" or "qwen3.5:4b-q4_k_m" => "~3.3 GB installed class (Q4)",
            "qwen3:4b-instruct" => "~2.5 GB (lighter instruction)",
            "phi4-mini" => "~2.5 GB class (varies by quantization)",
            "gemma3:4b" => "~3.3 GB class (varies by quantization)",
            "qwen3.5:2b" => "~1.5-2 GB fallback class",
            _ => null,
        };
    }

    private static IReadOnlyList<RecommendedModel> GetRecommendedCandidates(HardwareInfo hardware)
    {
        var has6GbVramClass = hardware.VramGbEstimate >= 5.5 || (hardware.GpuName?.Contains("3050", StringComparison.OrdinalIgnoreCase) == true);
        var has16GbRam = hardware.RamGb >= 14;
        var fit4b = has6GbVramClass && has16GbRam
            ? "good fit (4B Q4 class; 6GB VRAM / 16GB RAM target)"
            : "possible with tuning; verify VRAM/RAM";

        return
        [
            new RecommendedModel(
                "qwen3.5:4b",
                "~3.3 GB",
                "4096 (initial)",
                "vision, tools, strong general chat (capability depends on runtime/model build)",
                fit4b,
                "Best default for an RTX 3050 6GB / 16GB RAM class machine when installed."),
            new RecommendedModel(
                "qwen3:4b-instruct",
                "~2.5 GB",
                "4096 (initial)",
                "instruction following, chat (text-focused)",
                fit4b,
                "Lighter instruction-tuned alternative when you want lower memory pressure."),
            new RecommendedModel(
                "phi4-mini",
                "~2.5 GB (estimate)",
                "4096 (initial)",
                "compact reasoning/chat (capabilities vary by build)",
                "good on 6GB VRAM-class GPUs",
                "Small Microsoft model option; verify exact tag/capabilities in Ollama."),
            new RecommendedModel(
                "gemma3:4b",
                "~3.3 GB (estimate)",
                "4096 (initial)",
                "compact chat/instruction; some builds support multimodal features",
                "good on 6GB VRAM-class GPUs",
                "Google Gemma 3 compact option; verify exact tag and modality support."),
            new RecommendedModel(
                "qwen3.5:2b",
                "~1.5-2 GB (estimate)",
                "4096 (initial)",
                "lightweight chat/fallback",
                "best fallback for tighter VRAM/RAM",
                "Use when 4B models are too large or slow on the current machine."),
        ];
    }

    private sealed record RecommendedModel(
        string Name,
        string ApproxDownloadSize,
        string ContextInitial,
        string Capabilities,
        string Fit,
        string Reason)
    {
        public bool Installed { get; init; }
    }
}

/// <summary>
/// Best-effort local hardware detection. No data is uploaded.
/// </summary>
internal sealed record HardwareInfo(
    int? LogicalCores,
    double? RamGb,
    string? GpuName,
    double? VramGbEstimate,
    double? DiskFreeGb,
    string? Note)
{
    public static HardwareInfo Detect()
    {
        var noteParts = new List<string>();
        int? cores = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : null;

        double? ramGb = null;
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
            {
                ramGb = info.TotalAvailableMemoryBytes / (1024d * 1024d * 1024d);
            }
        }
        catch
        {
            noteParts.Add("RAM from GC memory info unavailable");
        }

        double? diskGb = null;
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                diskGb = drive.AvailableFreeSpace / (1024d * 1024d * 1024d);
            }
        }
        catch
        {
        }

        var (gpuName, vramGb) = TryDetectGpu();
        if (gpuName is null)
        {
            noteParts.Add("GPU/VRAM detection unavailable; values shown as estimates where inferred");
        }
        else if (!vramGb.HasValue)
        {
            noteParts.Add("VRAM size not reported by adapter; estimate may be inaccurate");
        }

        var note = noteParts.Count > 0 ? string.Join("; ", noteParts) + "." : null;
        return new HardwareInfo(cores, ramGb, gpuName, vramGb, diskGb, note);
    }

    private static (string? Name, double? VramGb) TryDetectGpu()
    {
        try
        {
            var output = RunProcess(
                "powershell",
                "-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_VideoController | Select-Object -First 1 Name,AdapterRAM | ConvertTo-Json -Compress\"",
                TimeSpan.FromSeconds(3));
            if (string.IsNullOrWhiteSpace(output))
            {
                return (null, null);
            }

            using var doc = System.Text.Json.JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            string? name = null;
            if (root.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                name = nameProp.GetString();
            }

            double? vram = null;
            if (root.TryGetProperty("AdapterRAM", out var ramProp)
                && ramProp.ValueKind == System.Text.Json.JsonValueKind.Number
                && ramProp.TryGetInt64(out var ramBytes)
                && ramBytes > 0)
            {
                vram = ramBytes / (1024d * 1024d * 1024d);
                // WMI AdapterRAM can be reported as 32-bit; treat very small/odd values cautiously.
                if (vram > 128)
                {
                    vram = null;
                }
            }

            if (vram is null && name?.Contains("3050", StringComparison.OrdinalIgnoreCase) == true)
            {
                vram = 6d; // best-effort inference for stated/common config; labeled estimate in output.
            }

            return (name, vram);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? RunProcess(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                },
            };
            process.Start();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd() : null;
        }
        catch
        {
            return null;
        }
    }
}
