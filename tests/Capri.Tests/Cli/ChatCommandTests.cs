using Capri.Cli;
using Capri.Cli.Services;
using Capri.Security;
using Capri.Tests.TestDoubles;
using Capri.Cli.Commands;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Core.Autonomy;
using NSubstitute;
using System.Runtime.CompilerServices;

namespace Capri.Tests.Cli;

public sealed class ChatCommandTests
{
    [Fact]
    public async Task ControlCCancelsStreamThatHasNotProducedAnyChunks()
    {
        using var temp = new TempDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new SilentProvider(entered);
        using var services = new StubCliServices(new CapriPaths(temp.Path),
            chatProvider: provider, modelRouter: new StubModelRouter());
        var console = new CancelConsole();
        var running = new ChatCommand(console, services).ExecuteAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        console.Cancel();
        Assert.Equal(CliExitCodes.Success, await running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(console.Output.Contains("[generation cancelled]", StringComparison.Ordinal), console.Output);
    }

    private static async IAsyncEnumerable<ChatDelta> NoChunks(TaskCompletionSource entered,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }

    private sealed class SilentProvider(TaskCompletionSource entered) : IChatProvider
    {
        public string ProviderId => "ollama";
        public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => NoChunks(entered, cancellationToken);
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CancelConsole : ICliConsole
    {
        private readonly System.Text.StringBuilder _output = new();
        private int _reads;
        private volatile bool _cancel;
        public string Output => _output.ToString();
        public bool IsInputRedirected => false;
        public bool IsOutputRedirected => false;
        public bool IsInteractive => true;
        public bool KeyAvailable => _cancel;
        public bool TreatControlCAsInput { get; set; }
        public void Cancel() => _cancel = true;
        public ConsoleKeyInfo ReadKey(bool intercept = false)
        {
            _cancel = false;
            return new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true);
        }
        public string? ReadLine() => ++_reads == 1 ? "hello" : "/exit";
        public Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReadLine());
        }
        public void Write(string text) => _output.Append(text);
        public void WriteLine(string text) => _output.AppendLine(text);
        public void WriteErrorLine(string text) => _output.AppendLine(text);
        public void Clear() { }
    }

    [Fact]
    public async Task ApprovalTimeoutCancelsInputWithoutLeavingAnOrphanRead()
    {
        var console = new BlockingConsole();
        var approval = new CliTaskApproval(console, TimeSpan.FromMilliseconds(80));
        var plan = TaskPlanParser.Parse("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}");
        Assert.Null(await approval.ApproveAsync(plan, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => console.ReadCancelled, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ConsoleWithoutAsyncInputFailsClosedInsteadOfBlockingReadLine()
    {
        ICliConsole console = new UnsupportedConsole();
        await Assert.ThrowsAsync<NotSupportedException>(() => console.ReadLineAsync(CancellationToken.None));
    }

    private sealed class UnsupportedConsole : ICliConsole
    {
        public bool IsInputRedirected => false;
        public bool IsOutputRedirected => false;
        public bool IsInteractive => true;
        public string? ReadLine() => throw new InvalidOperationException("Blocking input must not be invoked.");
        public void Write(string text) { }
        public void WriteLine(string text) { }
        public void WriteErrorLine(string text) { }
        public void Clear() { }
    }

    private sealed class BlockingConsole : ICliConsole
    {
        public bool ReadCancelled { get; private set; }
        public bool IsInputRedirected => false;
        public bool IsOutputRedirected => false;
        public bool IsInteractive => true;
        public string? ReadLine() => throw new InvalidOperationException("Synchronous reads must not be used.");
        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { ReadCancelled = true; throw; }
            return null;
        }
        public void Write(string text) { }
        public void WriteLine(string text) { }
        public void WriteErrorLine(string text) { }
        public void Clear() { }
    }
    [Fact]
    public async Task ChatRequiresInteractiveTerminal()
    {
        using var fixture = new ChatFixture();

        var exitCode = await fixture.RunAsync(CliParser.Parse(["chat"]));

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("interactive terminal", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatReportsNoModelConfigured()
    {
        var settings = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with { ProviderId = "ollama", BaseUrl = "http://127.0.0.1:11434/v1", ChatModel = string.Empty },
        };
        using var fixture = new ChatFixture(settings: settings, interactive: true);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["chat"]));

        Assert.Equal(CliExitCodes.Error, exitCode);
        Assert.Contains("No chat model is configured", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ChatFixture : IDisposable
    {
        private readonly TempDirectory _temp;

        public ChatFixture(Capri.Core.Models.AppSettings? settings = null, bool interactive = false)
        {
            _temp = new TempDirectory();
            var effective = settings ?? CapriDefaultSettings.Create() with
            {
                Provider = CapriDefaultSettings.Create().Provider with
                {
                    ProviderId = "ollama",
                    BaseUrl = "http://127.0.0.1:11434/v1",
                    ChatModel = "qwen3.5:4b",
                },
            };
            Services = new StubCliServices(
                new CapriPaths(_temp.Path),
                new FakeAppSettingsStore(effective),
                chatProvider: new StubChatProvider(),
                modelRouter: new StubModelRouter("ollama", effective.Provider.ChatModel));
            Console = new FakeCliConsole(interactive: interactive);
            Application = new CliApplication(Console, Services, new StubEndpointProbe(true), new StubDesktopLauncher(), new StubDpapiProbe(true), new StubOllamaClient());
        }

        public FakeCliConsole Console { get; }
        public StubCliServices Services { get; }
        public CliApplication Application { get; }
        public Task<int> RunAsync(CliInvocation invocation) => Application.RunAsync(invocation);
        public void Dispose() { Services.Dispose(); _temp.Dispose(); }
    }
}
