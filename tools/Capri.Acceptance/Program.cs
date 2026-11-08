using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Capri.Automation.Apps;
using Capri.Automation.Autonomy;
using Capri.Automation.Processes;
using Capri.Automation.Web;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Core.Models;
using Capri.Core.Providers;
using Capri.Desktop.Services;
using Capri.Providers;
using Capri.Security;

namespace Capri.Acceptance;

// Run separately from the production app; never acquires its mutex or stops its processes.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not ("config-status" or "live" or "windows-only"))
        {
            Console.Error.WriteLine("Usage: Capri.Acceptance config-status | live | windows-only (mock planner; real approval and Windows execution)");
            return 2;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = 1;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try { result = await RunAsync(args[0]); }
            catch
            {
                Console.Error.WriteLine("Acceptance unavailable; no exception details or secrets emitted.");
                if (args[0] == "windows-only")
                    Console.WriteLine(JsonSerializer.Serialize(new { mode = "windows-only", planner = "mock-fixed-plan-not-live-inference", phase = "Unavailable", result = "unavailable; action state unknown" }));
            }
            finally { app.Shutdown(); }
        }, DispatcherPriority.Normal);
        app.Run();
        return result;
    }

    private static async Task<int> RunAsync(string mode)
    {
        // This explicit mode does not inspect credentials or call a model. It still uses
        // the production approval dialog, coordinator, executor and Windows observer.
        if (mode == "windows-only") return await RunTaskAsync(mode);

        var paths = new CapriPaths();
        // No fallback settings: require explicit persisted provider, model and endpoint.
        var configured = false;
        string? providerId = null, model = null, endpoint = null;
        var allowCloudInference = false;
        if (File.Exists(paths.SettingsFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(paths.SettingsFile));
                var configuredProvider = doc.RootElement.GetProperty("provider");
                providerId = configuredProvider.GetProperty("providerId").GetString();
                model = configuredProvider.GetProperty("chatModel").GetString();
                endpoint = configuredProvider.GetProperty("baseUrl").GetString();
                allowCloudInference = configuredProvider.TryGetProperty("allowCloudInference", out var consent)
                    && consent.ValueKind == JsonValueKind.True;
                configured = !string.IsNullOrWhiteSpace(providerId) && !string.IsNullOrWhiteSpace(model)
                    && IsAcceptableLiveEndpoint(providerId, endpoint);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException) { }
        }
        using var credentials = new DpapiCredentialStore(paths);
        var profile = ProviderCatalog.TryGetById(providerId, out var resolvedProfile) ? resolvedProfile : null;
        var requiresCredential = profile is null || profile.RequiresApiKey;
        var hasCredential = await HasConfiguredCredentialAsync(credentials, providerId);
        // Status exposes only provider/model and credential presence; no URL, key or response body.
        Console.WriteLine(JsonSerializer.Serialize(new { provider = providerId, model, credentialExists = hasCredential }));
        if (mode == "config-status") return 0;
        if (!configured)
        {
            Console.Error.WriteLine("Live blocked: explicit supported provider, acceptable HTTPS or local-loopback endpoint, and model required.");
            return 3;
        }
        if ((profile is null || profile.IsCloud) && !allowCloudInference)
        {
            Console.Error.WriteLine("Live blocked: explicit cloud-inference consent is required for cloud providers.");
            return 3;
        }
        if (requiresCredential && !hasCredential)
        {
            Console.Error.WriteLine("Live blocked: the configured provider profile requires a DPAPI credential.");
            return 3;
        }

        return await RunTaskAsync(mode);
    }

    private static async Task<bool> HasConfiguredCredentialAsync(DpapiCredentialStore credentials, string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return false;

        var profileKey = ProviderCatalog.TryGetById(providerId, out var profile)
            ? ProviderCredentials.GetApiKeyCredentialKey(profile)
            : ProviderCredentials.GetApiKeyCredentialKey(providerId);
        if (await credentials.HasAsync(profileKey)) return true;

        // One-time compatibility: the OpenAI profile may still rely on the legacy shared key.
        return string.Equals(providerId, ProviderCatalog.OpenAiId, StringComparison.OrdinalIgnoreCase)
            && await credentials.HasAsync(ProviderCredentials.LegacyApiKeyCredentialKey);
    }

    private static bool IsAcceptableLiveEndpoint(string providerId, string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps) return true;

        // Local catalog profiles may use plain HTTP only on a loopback address.
        return uri.Scheme == Uri.UriSchemeHttp
            && ProviderCatalog.TryGetById(providerId, out var profile)
            && profile.IsLocal
            && ProviderCatalog.IsLoopbackAllowedHttp(endpoint);
    }

    private static async Task<int> RunTaskAsync(string mode)
    {
        // Allow a user time to find and review the foreground approval dialog.
        // Live-provider runs retain the shorter bounded execution deadline.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(mode == "windows-only" ? 300 : 120));
        var ct = deadline.Token;
        var name = "capri-acceptance-" + Guid.NewGuid().ToString("N") + ".txt";
        var text = "Capri acceptance note " + Guid.NewGuid().ToString("N");
        var workspace = Path.Combine(Path.GetTempPath(), "Capri-Acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace); // disposable; intentionally not automatically deleted
        using var settings = mode == "live" ? new JsonAppSettingsStore(new CapriPaths()) : null;
        using var credentials = mode == "live" ? new DpapiCredentialStore(new CapriPaths()) : null;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var mock = new FixedNotePlanner(name, text);
        IChatProvider provider = mode == "windows-only" ? mock : new OpenAiCompatibleChatProvider(http, settings!, credentials!);
        IModelRouter router = mode == "windows-only" ? mock : new SingleProviderModelRouter(provider, settings!);
        var approval = new ExactPlanApproval(new WpfTaskApproval(workspace), name, text);
        var starter = new ShellProcessStarter();
        var executor = new SafeTaskExecutor(new AppAliasResolver(), new AppLauncher(starter, new AppPathValidator()), new WebLauncher(starter));
        var coordinator = new TaskCoordinator(provider, router, approval, executor, new TaskMetadataJournal(new CapriPaths(workspace)));
        var phases = new List<string>();
        var windowObserved = false;
        await foreach (var progress in coordinator.RunAsync($"Create a NEW plain text note named {name} with EXACT literal text '{text}', then open that same note in Notepad. Return exactly two steps: create_note then open_note. No other actions.", ct))
        {
            phases.Add(progress.Phase.ToString());
            if (progress.Phase == TaskPhase.Observing && progress.Message.Contains("Notepad window observed", StringComparison.Ordinal)) windowObserved = true;
        }
        var note = Path.Combine(workspace, name);
        var diskVerified = File.Exists(note) && await File.ReadAllTextAsync(note) == text;
        // Metadata only: never include note text, destination, provider response or credential.
        Console.WriteLine(JsonSerializer.Serialize(new { mode, planner = mode == "windows-only" ? "mock-fixed-plan-not-live-inference" : "live-configured-provider", phase = phases.LastOrDefault(), approvalShown = approval.Shown, diskVerified, windowObserved, workspaceCreated = true, timedOut = deadline.IsCancellationRequested }));
        return phases.LastOrDefault() == nameof(TaskPhase.Completed) && diskVerified && windowObserved ? 0 : 4;
    }

    // Deterministic planner only; the resulting plan passes through the normal parser,
    // exact-plan gate, real user approval dialog and real executor. No network inference.
    private sealed class FixedNotePlanner(string name, string text) : IChatProvider, IModelRouter
    {
        public string ProviderId => "acceptance-mock-planner";

        public ValueTask<ModelRoute> ResolveAsync(ModelRouteRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ModelRoute(ProviderId, "fixed-note-plan", false));
        }

        public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            // The parser permits a text field only for create_note; omit it on open_note.
            var json = JsonSerializer.Serialize(new { steps = new object[] {
                new { tool = "create_note", target = name, text },
                new { tool = "open_note", target = name }
            } });
            return Task.FromResult(new ChatCompletion(new ChatMessage(ChatRole.Assistant, json), "fixed-note-plan"));
        }

        public IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("Fixed note planner does not stream.");

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderHealth(true, "Fixed note planner; no inference."));
    }

    private sealed class ExactPlanApproval(ITaskApproval inner, string name, string text) : ITaskApproval
    {
        public bool Shown { get; private set; }
        public async Task<string?> ApproveAsync(TaskPlan plan, CancellationToken ct)
        {
            if (plan.Steps.Count != 2 || plan.Steps[0] != new TaskStep(TaskTool.CreateNote, name, text) ||
                plan.Steps[1] != new TaskStep(TaskTool.OpenNote, name)) return null;
            Shown = true;
            return await inner.ApproveAsync(plan, ct);
        }
    }
}
