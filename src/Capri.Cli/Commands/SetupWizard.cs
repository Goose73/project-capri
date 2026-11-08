using System.Diagnostics;
using System.Globalization;
using Capri.Cli.Configuration;
using Capri.Cli.Services;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Core.Providers;

namespace Capri.Cli.Commands;

/// <summary>
/// Interactive first-run/repair setup wizard for the Capri CLI. Uses existing
/// shared settings, Ollama, endpoint probes, and chat provider services.
/// Testable via injected console and service doubles.
/// </summary>
public sealed class SetupWizard
{
    private const string ProbePrompt = "Reply with: Capri ready";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly string[] ThemeChoices = ["System", "Light", "Dark"];

    private readonly ICliConsole _console;
    private readonly ICliServices _services;
    private readonly IEndpointProbe _endpointProbe;
    private readonly IOllamaClient? _ollama;
    private readonly bool _repair;

    public SetupWizard(
        ICliConsole console,
        ICliServices services,
        IEndpointProbe endpointProbe,
        bool repair)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _endpointProbe = endpointProbe ?? throw new ArgumentNullException(nameof(endpointProbe));
        _ollama = services.OllamaClient;
        _repair = repair;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_console.IsInteractive)
        {
            _console.WriteErrorLine($"capri: `capri setup{(_repair ? " --repair" : string.Empty)}` is an interactive wizard and requires a terminal.");
            _console.WriteErrorLine("Use `capri config`, `capri models`, `capri providers test`, and `capri doctor` for non-interactive configuration and diagnostics.");
            return CliExitCodes.Usage;
        }

        WriteHeader();
        var settings = _services.Settings.Current;
        if (ShouldResumeOrRepair(settings))
        {
            var action = PromptResumeAction(settings);
            if (action == ResumeAction.Cancel)
            {
                _console.WriteLine("Setup cancelled. No changes were made.");
                return CliExitCodes.Success;
            }

            if (action == ResumeAction.DiagnosticsOnly)
            {
                await RunDiagnosticsAsync(settings, cancellationToken).ConfigureAwait(false);
                _console.WriteLine(string.Empty);
                _console.WriteLine("Diagnostics complete. Run `capri setup --repair` to reconfigure.");
                return CliExitCodes.Success;
            }
        }

        // Step 1: hardware
        var hardware = DetectHardwareStep();
        if (IsCancelled(hardware)) { return SuccessCancelled(); }

        // Step 2: detect local providers/models
        var localState = await DetectLocalProvidersAsync(settings, cancellationToken).ConfigureAwait(false);

        // Step 3+4: recommend/select/download/use model
        var working = _services.Settings.Current;
        var modelResult = await ConfigureModelAsync(working, hardware.Info, localState, cancellationToken).ConfigureAwait(false);
        if (modelResult.Cancelled) { return SuccessCancelled(); }
        working = modelResult.Settings;

        // Step 5: inference test (genuine, uses configured provider/model)
        InferenceTestResult inference = new(false, 0, "skipped", null);
        if (!string.IsNullOrWhiteSpace(working.Provider.ChatModel))
        {
            inference = await RunInferenceTestAsync(working, persistModelSelection: true, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _console.WriteLine("Inference test skipped: no model is configured.");
        }

        // Step 6: voice preferences (config only)
        working = ConfigureVoice(working);

        // Step 7: UI and automation prefs
        working = ConfigureUiAndAutomation(working);

        // Step 8: save
        if (!await SaveSettingsAsync(working, cancellationToken).ConfigureAwait(false))
        {
            _console.WriteErrorLine("capri: settings could not be saved. Run `capri doctor` for guidance.");
            return CliExitCodes.Error;
        }
        working = _services.Settings.Current;

        // Step 9: optional intro conversation
        if (inference.Success)
        {
            await MaybeRunIntroConversationAsync(working, cancellationToken).ConfigureAwait(false);
        }

        // Step 10: summary
        PrintSummary(working, inference, localState);
        return CliExitCodes.Success;
    }

    private void WriteHeader()
    {
        _console.WriteLine(_repair ? "Capri setup (repair mode)" : "Capri setup wizard");
        _console.WriteLine("This wizard configures local/shared settings. Secrets are never displayed.");
        _console.WriteLine("Local AI (Ollama) does not require an API key. Cloud consent is never enabled silently.");
        _console.WriteLine("Type 'b' to go back where offered, 's' to skip, or 'c' to cancel.");
        _console.WriteLine(string.Empty);
    }

    private bool ShouldResumeOrRepair(AppSettings settings)
    {
        return _repair
            || !string.IsNullOrWhiteSpace(settings.Provider.ProviderId)
            || !string.IsNullOrWhiteSpace(settings.Provider.ChatModel)
            || settings.Voice.Enabled
            || settings.Automation.Enabled != true; // default true, but presence of config state is best-effort
    }

    private ResumeAction PromptResumeAction(AppSettings settings)
    {
        _console.WriteLine("Existing configuration detected:");
        _console.WriteLine($"  Provider: {Display(settings.Provider.ProviderId)}");
        _console.WriteLine($"  Model:    {Display(settings.Provider.ChatModel)}");
        _console.WriteLine($"  Base URL: {SafeUrl.ForDisplay(settings.Provider.BaseUrl)}");
        _console.WriteLine($"  Voice:    {(settings.Voice.Enabled ? "enabled (config)" : "disabled")}");
        _console.WriteLine(string.Empty);
        _console.WriteLine("Choose: [1] Continue/reconfigure, [2] Repair diagnostics only, [3] Cancel");
        while (true)
        {
            _console.Write("Selection [1]: ");
            var input = ReadInput();
            if (IsCancel(input)) return ResumeAction.Cancel;
            if (string.IsNullOrWhiteSpace(input) || input.Trim() == "1") return ResumeAction.Reconfigure;
            if (input.Trim() == "2") return ResumeAction.DiagnosticsOnly;
            if (input.Trim() == "3") return ResumeAction.Cancel;
            _console.WriteLine("Enter 1, 2, or 3.");
        }
    }

    private HardwareStepResult DetectHardwareStep()
    {
        _console.WriteLine("Step 1/7 - Hardware detection (best-effort, local only)");
        var info = HardwareInfo.Detect();
        _console.WriteLine($"  Windows platform: {Environment.OSVersion.VersionString}");
        _console.WriteLine($"  CPU logical cores: {info.LogicalCores?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        _console.WriteLine($"  RAM:               {FormatGb(info.RamGb)} (estimate when unavailable)");
        _console.WriteLine($"  GPU:               {info.GpuName ?? "unknown"}");
        _console.WriteLine($"  VRAM:              {FormatGb(info.VramGbEstimate)} (best-effort estimate)");
        _console.WriteLine($"  Disk free:         {FormatGb(info.DiskFreeGb)}");
        if (!string.IsNullOrWhiteSpace(info.Note))
        {
            _console.WriteLine($"  Note:              {info.Note}");
        }
        _console.WriteLine(string.Empty);
        return new HardwareStepResult(info);
    }

    private async Task<LocalProviderState> DetectLocalProvidersAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        _console.WriteLine("Step 2/7 - Local provider detection");
        var targets = LocalEndpoints.GetTargets(settings);
        var reachable = new Dictionary<string, EndpointProbeResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(target.BaseUrl, UriKind.Absolute, out var uri))
            {
                _console.WriteLine($"  {target.Name}: invalid URL ({SafeUrl.ForDisplay(target.BaseUrl)})");
                reachable[target.Name] = new EndpointProbeResult(false, "invalid URL", TimeSpan.Zero);
                continue;
            }
            var probe = await _endpointProbe.ProbeAsync(uri, ProbeTimeout, cancellationToken).ConfigureAwait(false);
            reachable[target.Name] = probe;
            _console.WriteLine($"  {target.Name}: {(probe.Reachable ? "reachable" : "not reachable")} ({probe.Detail})");
        }

        IReadOnlyList<OllamaModelInfo> ollamaModels = [];
        string? ollamaNote = null;
        if (_ollama is null)
        {
            ollamaNote = "Ollama client unavailable in this CLI build.";
            _console.WriteLine($"  Ollama models: {ollamaNote}");
        }
        else
        {
            try
            {
                var baseUrl = ResolveOllamaBaseUrl(settings);
                ollamaModels = await _ollama.ListModelsAsync(baseUrl, cancellationToken).ConfigureAwait(false);
                _console.WriteLine($"  Ollama installed models: {ollamaModels.Count}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ollamaNote = $"Could not query Ollama models ({ex.GetType().Name}).";
                _console.WriteLine($"  Ollama models: {ollamaNote}");
            }
        }
        _console.WriteLine(string.Empty);
        return new LocalProviderState(reachable, ollamaModels, ollamaNote);
    }

    private async Task<ModelConfigResult> ConfigureModelAsync(
        AppSettings settings,
        HardwareInfo hardware,
        LocalProviderState localState,
        CancellationToken cancellationToken)
    {
        _console.WriteLine("Step 3/7 - Model recommendation");
        var candidates = GetRecommendedCandidates(hardware, localState.OllamaModels);
        for (var i = 0; i < candidates.Length; i++)
        {
            var c = candidates[i];
            var installed = c.Installed ? " [installed]" : string.Empty;
            _console.WriteLine($"  [{i + 1}] {c.Name}{installed} - {c.ApproxDownloadSize}; {c.Fit}");
            _console.WriteLine($"      {c.Reason}");
        }
        _console.WriteLine(string.Empty);
        _console.WriteLine("Step 4/7 - Choose model");
        _console.WriteLine("Options: enter number to use/download recommendation, enter installed model name, 's' skip, 'c' cancel.");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _console.Write("Model choice: ");
            var input = ReadInput();
            if (IsCancel(input)) return ModelConfigResult.CancelledResult(settings);
            if (IsSkip(input))
            {
                _console.WriteLine("Skipped model configuration.");
                _console.WriteLine(string.Empty);
                return new ModelConfigResult(settings, false);
            }

            if (string.IsNullOrWhiteSpace(input)) { continue; }

            string? selected = null;
            RecommendedCandidate? selectedCandidate = null;
            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= candidates.Length)
            {
                selectedCandidate = candidates[index - 1];
                selected = selectedCandidate.Name;
            }
            else
            {
                selected = input.Trim();
                if (!TryValidateModelName(selected, out var modelErr))
                {
                    _console.WriteLine($"Invalid model name: {modelErr}");
                    continue;
                }
            }

            var isInstalled = localState.OllamaModels.Any(m => IsModelMatch(m.Name, selected));

            if (!isInstalled)
            {
                var size = selectedCandidate?.ApproxDownloadSize ?? GetKnownApproxSize(selected) ?? "size varies (estimate unavailable)";
                var downloaded = await OfferDownloadAsync(selected, size, cancellationToken).ConfigureAwait(false);
                if (downloaded == DownloadOutcome.Cancelled) return ModelConfigResult.CancelledResult(settings);
                if (downloaded == DownloadOutcome.Skipped)
                {
                    _console.WriteLine("No model selected/installed. You can run `capri models pull <model>` later.");
                    continue;
                }
                // after download, verify best-effort by refreshing? not necessary; proceed to save.
                isInstalled = true;
            }

            var updated = ApplyLocalModel(settings, selected);
            _console.WriteLine($"Selected model: {selected} (provider: {updated.Provider.ProviderId}, base URL: {SafeUrl.ForDisplay(updated.Provider.BaseUrl)})");
            _console.WriteLine(string.Empty);
            return new ModelConfigResult(updated, false);
        }
    }

    private async Task<DownloadOutcome> OfferDownloadAsync(string modelName, string approxSize, CancellationToken cancellationToken)
    {
        if (_ollama is null)
        {
            _console.WriteLine("Cannot download: Ollama client unavailable. Install/select an existing model manually.");
            return DownloadOutcome.Skipped;
        }

        _console.WriteLine($"Model not installed locally: {modelName}");
        _console.WriteLine($"Exact model tag: {modelName}");
        _console.WriteLine($"Estimated download size: {approxSize} (best-known estimate; actual size may vary)");
        _console.WriteLine("Capri will not download without explicit confirmation.");
        _console.Write("Download now? [y/N/c]: ");
        var answer = ReadInput();
        if (IsCancel(answer)) return DownloadOutcome.Cancelled;
        if (!IsYes(answer))
        {
            _console.WriteLine("Download skipped.");
            return DownloadOutcome.Skipped;
        }

        try
        {
            await foreach (var progress in _ollama.PullModelAsync(ResolveOllamaBaseUrl(_services.Settings.Current), modelName, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(progress.Error))
                {
                    _console.WriteErrorLine($"capri: model download failed: {progress.Error}");
                    return DownloadOutcome.Skipped;
                }
                var line = FormatPullProgress(progress);
                if (line is not null) _console.WriteLine(line);
            }
            _console.WriteLine($"Download completed: {modelName}");
            return DownloadOutcome.Downloaded;
        }
        catch (OperationCanceledException)
        {
            _console.WriteLine("Download cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _console.WriteErrorLine($"capri: download failed ({ex.GetType().Name}). Ensure Ollama is running.");
            return DownloadOutcome.Skipped;
        }
    }

    private async Task<InferenceTestResult> RunInferenceTestAsync(AppSettings settings, bool persistModelSelection, CancellationToken cancellationToken)
    {
        _console.WriteLine("Step 5/7 - Genuine inference test");
        _console.WriteLine($"  Sending actual request to configured provider/model: {settings.Provider.ProviderId}/{settings.Provider.ChatModel}");
        _console.WriteLine($"  Prompt: \"{ProbePrompt}\"");

        if (!ChatCommandSupport.TryGetConfiguredChat(_services, out var provider, out var router, out var configError))
        {
            _console.WriteErrorLine($"  Result: FAIL - {configError}");
            _console.WriteLine(string.Empty);
            return new InferenceTestResult(false, 0, configError ?? "chat unavailable", null);
        }

        // The router reads the settings store. Only reconfiguration needs an early save;
        // diagnostics must never write, even when the on-disk file is damaged.
        if (persistModelSelection && settings.Provider != _services.Settings.Current.Provider)
        {
            if (!CanSaveSettings())
            {
                _console.WriteErrorLine("  Result: FAIL - model configuration was not saved; inference test skipped.");
                return new InferenceTestResult(false, 0, "settings file problem", null);
            }
            try
            {
                await _services.Settings.UpdateAsync(settings, cancellationToken).ConfigureAwait(false);
                _console.WriteLine("  Model configuration saved before inference test. Cancelling setup later will not undo this save.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _console.WriteErrorLine($"  Result: FAIL - could not save model configuration ({ex.GetType().Name}); inference test skipped.");
                return new InferenceTestResult(false, 0, "model configuration could not be saved", null);
            }
        }

        ModelRoute route;
        try { route = await router!.ResolveAsync(new ModelRouteRequest(), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _console.WriteErrorLine($"  Result: FAIL - route resolution failed ({ex.GetType().Name}).");
            _console.WriteLine(string.Empty);
            return new InferenceTestResult(false, 0, ex.Message, null);
        }

        if (string.IsNullOrWhiteSpace(route.ModelId))
        {
            _console.WriteErrorLine("  Result: FAIL - no model resolved.");
            _console.WriteLine(string.Empty);
            return new InferenceTestResult(false, 0, "no model", null);
        }

        var request = new ChatRequest(
            [new ChatMessage(ChatRole.User, ProbePrompt)],
            ModelId: route.ModelId,
            Temperature: 0,
            MaxOutputTokens: 32);
        var sw = Stopwatch.StartNew();
        try
        {
            var completion = await provider!.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            if (completion.Error is not null)
            {
                var msg = ChatCommandSupport.DescribeProviderError(completion.Error, _services.Settings.Current.Provider);
                _console.WriteErrorLine($"  Result: FAIL ({sw.ElapsedMilliseconds} ms) - {msg}");
                _console.WriteLine(string.Empty);
                return new InferenceTestResult(false, (int)sw.ElapsedMilliseconds, msg, null);
            }
            var text = completion.Message.Content?.Trim() ?? string.Empty;
            var ok = text.Contains("Capri ready", StringComparison.OrdinalIgnoreCase);
            _console.WriteLine($"  Result: {(ok ? "PASS" : "PASS (response mismatch)") } ({sw.ElapsedMilliseconds} ms)");
            _console.WriteLine($"  Response: {text}");
            _console.WriteLine(string.Empty);
            return new InferenceTestResult(true, (int)sw.ElapsedMilliseconds, ok ? "ok" : "response mismatch", text);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            _console.WriteErrorLine($"  Result: FAIL ({sw.ElapsedMilliseconds} ms) - {CliErrors.Describe(ex)}");
            _console.WriteLine(string.Empty);
            return new InferenceTestResult(false, (int)sw.ElapsedMilliseconds, ex.Message, null);
        }
    }

    private AppSettings ConfigureVoice(AppSettings settings)
    {
        _console.WriteLine("Step 6/7 - Voice preferences (configuration only)");
        _console.WriteLine("  Note: \"Hey Capri\" wake activation is NOT yet implemented/validated in this build. These settings are saved for future use.");
        var voice = settings.Voice;
        voice = voice with { Enabled = PromptYesNo("Enable voice features (config only)?", voice.Enabled) };
        voice = voice with { EnableWakeWord = PromptYesNo("Enable wake word flag (config only; Hey Capri activation not yet implemented)?", voice.EnableWakeWord) };
        voice = voice with { WakeWord = PromptText("Wake word text", voice.WakeWord) ?? voice.WakeWord };
        voice = voice with { Language = PromptText("Language tag", voice.Language) ?? voice.Language };
        _console.WriteLine(string.Empty);
        return settings with { Voice = voice };
    }

    private AppSettings ConfigureUiAndAutomation(AppSettings settings)
    {
        _console.WriteLine("Step 7/7 - Desktop appearance/startup and automation permissions");
        var ui = settings.Ui;
        ui = ui with { StartMinimized = PromptYesNo("Start minimized?", ui.StartMinimized) };
        ui = ui with { AlwaysOnTop = PromptYesNo("Always on top?", ui.AlwaysOnTop) };
        ui = ui with { Theme = PromptChoice("Theme", ui.Theme, ThemeChoices) ?? ui.Theme };
        ui = ui with { ReduceMotion = PromptYesNo("Reduce avatar motion?", ui.ReduceMotion) };
        var auto = settings.Automation;
        auto = auto with { Enabled = PromptYesNo("Enable automation features?", auto.Enabled) };
        auto = auto with { AllowAppLaunch = PromptYesNo("Allow app launch?", auto.AllowAppLaunch) };
        auto = auto with { AllowWebLaunch = PromptYesNo("Allow web launch?", auto.AllowWebLaunch) };
        auto = auto with { AllowMediaControl = PromptYesNo("Allow media control?", auto.AllowMediaControl) };
        auto = auto with { RequireConfirmation = PromptYesNo("Require confirmation before user-visible actions?", auto.RequireConfirmation) };
        _console.WriteLine(string.Empty);
        // Never change cloud consent silently.
        return settings with { Ui = ui, Automation = auto };
    }

    private async Task<bool> SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!CanSaveSettings()) return false;
        try
        {
            await _services.Settings.UpdateAsync(settings, cancellationToken).ConfigureAwait(false);
            _console.WriteLine($"Settings saved: {_services.Paths.SettingsFile}");
            _console.WriteLine(string.Empty);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _console.WriteErrorLine($"capri: failed to save settings ({ex.GetType().Name}).");
            return false;
        }
    }

    private bool CanSaveSettings()
    {
        var inspection = SettingsFileInspector.Inspect(_services.Paths);
        if (inspection.State is SettingsFileState.Empty or SettingsFileState.Malformed or SettingsFileState.Unreadable)
        {
            _console.WriteErrorLine($"capri: settings file problem: {inspection.Detail}");
            return false;
        }
        return true;
    }

    private async Task MaybeRunIntroConversationAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        _console.Write("Run a short introductory conversation now? [y/N]: ");
        var answer = ReadInput();
        if (!IsYes(answer) || IsCancel(answer))
        {
            _console.WriteLine("Intro conversation skipped.");
            _console.WriteLine(string.Empty);
            return;
        }

        if (!ChatCommandSupport.TryGetConfiguredChat(_services, out var provider, out var router, out var error))
        {
            _console.WriteLine($"Intro conversation unavailable: {error}");
            _console.WriteLine(string.Empty);
            return;
        }

        ModelRoute route;
        try { route = await router!.ResolveAsync(new ModelRouteRequest(), cancellationToken).ConfigureAwait(false); }
        catch { _console.WriteLine("Intro conversation unavailable: could not resolve model."); _console.WriteLine(string.Empty); return; }
        if (string.IsNullOrWhiteSpace(route.ModelId)) { _console.WriteLine("Intro conversation unavailable: no model."); _console.WriteLine(string.Empty); return; }

        _console.WriteLine("Intro conversation (2 turns max, in-memory only). Type 's' to stop.");
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "You are Capri, a helpful local Windows assistant. Keep responses concise."),
            new(ChatRole.User, "Introduce yourself briefly in one sentence.")
        };
        for (var turn = 0; turn < 2; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await CompleteIntroTurnAsync(provider!, route, history, cancellationToken).ConfigureAwait(false);
            if (text is null) break;
            _console.WriteLine($"Capri: {text}");
            history.Add(new ChatMessage(ChatRole.Assistant, text));
            if (turn == 1) break;
            _console.Write("You (or 's'): ");
            var user = ReadInput();
            if (string.IsNullOrWhiteSpace(user) || IsSkip(user) || IsCancel(user)) break;
            history.Add(new ChatMessage(ChatRole.User, user));
        }
        _console.WriteLine(string.Empty);
    }

    private static async Task<string?> CompleteIntroTurnAsync(IChatProvider provider, ModelRoute route, List<ChatMessage> history, CancellationToken cancellationToken)
    {
        try
        {
            var request = new ChatRequest(history.ToArray(), ModelId: route.ModelId, Temperature: 0.4, MaxOutputTokens: 120);
            var completion = await provider.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            if (completion.Error is not null) return null;
            return completion.Message.Content?.Trim();
        }
        catch { return null; }
    }

    private void PrintSummary(AppSettings settings, InferenceTestResult inference, LocalProviderState localState)
    {
        _console.WriteLine("Setup summary");
        _console.WriteLine($"  Provider:        {Display(settings.Provider.ProviderId)}");
        _console.WriteLine($"  Model:           {Display(settings.Provider.ChatModel)}");
        _console.WriteLine($"  Base URL:        {SafeUrl.ForDisplay(settings.Provider.BaseUrl)}");
        _console.WriteLine($"  Inference test:  {(inference.Success ? $"PASS ({inference.LatencyMs} ms)" : $"FAIL/SKIPPED - {inference.Message}")}");
        _console.WriteLine($"  Voice enabled:   {settings.Voice.Enabled} (config only; Hey Capri wake activation not yet implemented/validated)");
        _console.WriteLine($"  Wake word flag:  {settings.Voice.EnableWakeWord}, text: {settings.Voice.WakeWord}");
        _console.WriteLine($"  UI theme:        {settings.Ui.Theme}, start minimized: {settings.Ui.StartMinimized}, always on top: {settings.Ui.AlwaysOnTop}");
        _console.WriteLine($"  Automation:      enabled={settings.Automation.Enabled}, confirm={settings.Automation.RequireConfirmation}");
        _console.WriteLine($"  Cloud consent:   not enabled silently (AllowCloudInference={settings.Provider.AllowCloudInference})");
        _console.WriteLine(string.Empty);
        _console.WriteLine("GUI access: the desktop app can launch/access setup through CLI/desktop integration (`capri setup` from terminal; desktop integration may be added where supported).");
        _console.WriteLine("Next commands:");
        _console.WriteLine("  capri ask \"hello\"");
        _console.WriteLine("  capri chat");
        _console.WriteLine("  capri models list");
        _console.WriteLine("  capri models recommend");
        _console.WriteLine("  capri providers test");
        _console.WriteLine("  capri start");
    }

    private async Task RunDiagnosticsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        _console.WriteLine("Repair diagnostics (best-effort):");
        await DetectLocalProvidersAsync(settings, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(settings.Provider.ChatModel))
        {
            await RunInferenceTestAsync(settings, persistModelSelection: false, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _console.WriteLine("No model configured; inference test skipped.");
        }
    }

    private bool PromptYesNo(string prompt, bool defaultValue)
    {
        var suffix = defaultValue ? "[Y/n/s/c]" : "[y/N/s/c]";
        while (true)
        {
            _console.Write($"{prompt} {suffix}: ");
            var input = ReadInput();
            if (IsCancel(input)) return defaultValue; // keep current on cancel within step; wizard cancel handled at major points
            if (IsSkip(input) || string.IsNullOrWhiteSpace(input)) return defaultValue;
            if (IsYes(input)) return true;
            if (IsNo(input)) return false;
            _console.WriteLine("Enter y, n, s (skip/keep), or c (keep current).");
        }
    }

    private string? PromptText(string prompt, string current)
    {
        _console.Write($"{prompt} [{current}] (s keep, c keep): ");
        var input = ReadInput();
        if (string.IsNullOrWhiteSpace(input) || IsSkip(input) || IsCancel(input)) return current;
        return input.Trim();
    }

    private string? PromptChoice(string prompt, string current, IReadOnlyList<string> choices)
    {
        _console.Write($"{prompt} ({string.Join("/", choices)}) [{current}]: ");
        var input = ReadInput();
        if (string.IsNullOrWhiteSpace(input) || IsSkip(input) || IsCancel(input)) return current;
        var match = choices.FirstOrDefault(c => string.Equals(c, input.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;
        _console.WriteLine($"Unknown choice; keeping {current}.");
        return current;
    }

    private string? ReadInput() => _console.ReadLine();

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "(not set)" : value;
    private static bool IsYes(string? v) => string.Equals(v?.Trim(), "y", StringComparison.OrdinalIgnoreCase) || string.Equals(v?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    private static bool IsNo(string? v) => string.Equals(v?.Trim(), "n", StringComparison.OrdinalIgnoreCase) || string.Equals(v?.Trim(), "no", StringComparison.OrdinalIgnoreCase);
    private static bool IsSkip(string? v) => string.Equals(v?.Trim(), "s", StringComparison.OrdinalIgnoreCase) || string.Equals(v?.Trim(), "skip", StringComparison.OrdinalIgnoreCase) || string.Equals(v?.Trim(), "b", StringComparison.OrdinalIgnoreCase) || string.Equals(v?.Trim(), "back", StringComparison.OrdinalIgnoreCase);
    private static bool IsCancel(string? v) => string.Equals(v?.Trim(), "c", StringComparison.OrdinalIgnoreCase) || string.Equals(v?.Trim(), "cancel", StringComparison.OrdinalIgnoreCase) || string.Equals(v?.Trim(), "q", StringComparison.OrdinalIgnoreCase);
    private static bool IsCancelled(HardwareStepResult _) => false;
    private int SuccessCancelled() { _console.WriteLine("Setup cancelled. No changes were made."); return CliExitCodes.Success; }

    private static string ResolveOllamaBaseUrl(AppSettings settings)
    {
        var provider = settings.Provider;
        if (!string.IsNullOrWhiteSpace(provider.BaseUrl)
            && Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri)
            && uri.IsLoopback
            && (uri.Port == 11434 || provider.ProviderId.Equals(ProviderCatalog.OllamaId, StringComparison.OrdinalIgnoreCase)))
        {
            return provider.BaseUrl;
        }
        return ProviderCatalog.Ollama.DefaultBaseUrl;
    }

    private static AppSettings ApplyLocalModel(AppSettings settings, string modelName)
    {
        var provider = settings.Provider;
        // Wizard model selection uses Ollama/local flow; configure Ollama defaults for local use.
        var useOllama = ProviderCatalog.OllamaId;
        var baseUrl = ProviderCatalog.Ollama.DefaultBaseUrl;
        if (!string.IsNullOrWhiteSpace(provider.BaseUrl)
            && ProviderCatalog.IsLoopbackAllowedHttp(provider.BaseUrl)
            && Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri)
            && uri.Port == 11434)
        {
            baseUrl = provider.BaseUrl.Trim();
        }

        return settings with
        {
            Provider = provider with
            {
                ProviderId = useOllama,
                BaseUrl = baseUrl,
                ChatModel = modelName,
                // Keep safe defaults; never enable cloud consent silently.
                AllowCloudInference = false,
                CloudConsentRecordedUtc = provider.CloudConsentRecordedUtc,
            }
        };
    }

    private static RecommendedCandidate[] GetRecommendedCandidates(HardwareInfo hardware, IReadOnlyList<OllamaModelInfo> installed)
    {
        var installedNames = new HashSet<string>(installed.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        var has6GbVramClass = hardware.VramGbEstimate >= 5.5 || (hardware.GpuName?.Contains("3050", StringComparison.OrdinalIgnoreCase) == true);
        var has16GbRam = hardware.RamGb >= 14;
        var fit4b = has6GbVramClass && has16GbRam ? "good fit (4B Q4 class; 6GB VRAM / 16GB RAM target)" : "possible with tuning; verify VRAM/RAM";
        var list = new[]
        {
            new RecommendedCandidate("qwen3.5:4b", "~3.3 GB", fit4b, "Best default for RTX 3050 6GB / 16GB RAM class when installed."),
            new RecommendedCandidate("qwen3:4b-instruct", "~2.5 GB", fit4b, "Lighter instruction-tuned alternative."),
            new RecommendedCandidate("phi4-mini", "~2.5 GB (estimate)", "good on 6GB VRAM-class GPUs", "Small model option; verify exact tag."),
            new RecommendedCandidate("gemma3:4b", "~3.3 GB (estimate)", "good on 6GB VRAM-class GPUs", "Compact option; verify tag/modality."),
            new RecommendedCandidate("qwen3.5:2b", "~1.5-2 GB (estimate)", "best fallback for tighter VRAM/RAM", "Fallback for smaller machines."),
        };
        return list.Select(c => c with { Installed = installedNames.Contains(c.Name) }).ToArray();
    }

    private static bool IsModelMatch(string installedName, string selected)
    {
        return string.Equals(installedName, selected, StringComparison.OrdinalIgnoreCase);
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

    private static string? FormatPullProgress(OllamaPullProgress progress)
    {
        if (string.Equals(progress.Status, "success", StringComparison.OrdinalIgnoreCase)) return "  status: success";
        if (progress.Total is > 0 && progress.Completed.HasValue)
        {
            var pct = Math.Clamp(progress.Completed.Value * 100d / progress.Total.Value, 0d, 100d);
            return $"  {progress.Status}: {pct:0.#}% ({FormatBytes(progress.Completed)}/{FormatBytes(progress.Total)})";
        }
        if (progress.Completed.HasValue) return $"  {progress.Status}: {FormatBytes(progress.Completed)}";
        if (!string.IsNullOrWhiteSpace(progress.Status)) return $"  {progress.Status}";
        return null;
    }

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0) return "unknown size";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes.Value} {units[unit]}" : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    private static string FormatGb(double? gb) => gb.HasValue ? $"{gb.Value.ToString("0.#", CultureInfo.InvariantCulture)} GB" : "unknown";

    private static bool TryValidateModelName(string? value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) { error = "A model name is required."; return false; }
        var trimmed = value.Trim();
        if (trimmed.Length > 200) { error = "Model name is too long."; return false; }
        if (trimmed.Any(char.IsWhiteSpace) || trimmed.Any(char.IsControl)) { error = "Model name must not contain whitespace or control characters."; return false; }
        if (trimmed.Contains("..", StringComparison.Ordinal) || trimmed.StartsWith('/') || trimmed.StartsWith('\\')) { error = "Model name is not valid."; return false; }
        return true;
    }

    private enum ResumeAction { Reconfigure, DiagnosticsOnly, Cancel }
    private enum DownloadOutcome { Downloaded, Skipped, Cancelled }
    private sealed record HardwareStepResult(HardwareInfo Info);
    private sealed record LocalProviderState(IReadOnlyDictionary<string, EndpointProbeResult> Reachability, IReadOnlyList<OllamaModelInfo> OllamaModels, string? OllamaNote);
    private sealed record RecommendedCandidate(string Name, string ApproxDownloadSize, string Fit, string Reason) { public bool Installed { get; init; } }
    private sealed record ModelConfigResult(AppSettings Settings, bool Cancelled) { public static ModelConfigResult CancelledResult(AppSettings s) => new(s, true); }
    private sealed record InferenceTestResult(bool Success, int LatencyMs, string Message, string? Response);
}
