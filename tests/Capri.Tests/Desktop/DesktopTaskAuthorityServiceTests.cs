using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;
using Capri.Core.Models;
using Capri.Desktop.Services;
using Capri.Security;
using Capri.Security.Autonomy;
using Capri.Security.Autonomy.Authority;
using Capri.Tests.Cli;
using Capri.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Capri.Tests.Desktop;

public sealed class DesktopTaskAuthorityServiceTests
{
    [Fact]
    public async Task HostingDesktopService_BridgesApprovalToUiAndCompletesCliSubmittedTask()
    {
        await using var fx = await DesktopAuthorityFixture.CreateAsync("""{"steps":[{"tool":"launch_app","target":"notepad"}]}""");
        var bridge = new FakeUiBridge(_ => new AuthorityApprovalDecision(true, null));
        var service = fx.CreateService(bridge);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => service.ConnectionStatus.Contains("Hosting", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(10));

            await using var cli = new NamedPipeTaskAuthorityClient(fx.PipeName, "cli-test", canApprove: false, connectTimeout: TimeSpan.FromSeconds(5));
            await cli.EnsureConnectedAsync();
            var submit = await cli.SendAsync(new AuthorityRequest("s1", AuthorityRequestKind.SubmitTask, Goal: "open notepad"));
            Assert.True(submit.Ok);
            var taskId = submit.Task!.TaskId;

            var terminal = await WaitTerminalViaClientAsync(cli, taskId, TimeSpan.FromSeconds(20));
            Assert.NotNull(bridge.LastRequest);
            Assert.Equal(taskId, bridge.LastRequest!.TaskId);
            Assert.Equal(TaskAuthorityProtocol.ComputePlanHash(bridge.LastRequest.Plan), bridge.LastRequest.PlanHash);
            Assert.Equal("Completed", terminal!.Phase);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HostingDesktopService_KeepsLastAuthoritativeStateWhileTaskEventsArrive()
    {
        await using var fx = await DesktopAuthorityFixture.CreateAsync("""{"steps":[{"tool":"launch_app","target":"notepad"}]}""");
        var bridge = new FakeUiBridge(_ => new AuthorityApprovalDecision(false, null));
        var service = fx.CreateService(bridge);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => service.LastState is not null, TimeSpan.FromSeconds(10));
            await using var cli = new NamedPipeTaskAuthorityClient(fx.PipeName, "cli-state-test", canApprove: false, connectTimeout: TimeSpan.FromSeconds(5));
            await cli.EnsureConnectedAsync();
            var submit = await cli.SendAsync(new AuthorityRequest("state-submit", AuthorityRequestKind.SubmitTask, Goal: "inspect state"));
            Assert.True(submit.Ok);
            var terminal = await WaitTerminalViaClientAsync(cli, submit.Task!.TaskId, TimeSpan.FromSeconds(20));
            Assert.NotNull(terminal);
            // The desktop should not lose its last authoritative state when progress
            // and task-completion notifications are interleaved with state updates.
            Assert.NotNull(service.LastState);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HostingDesktopService_UiDenialFailsTaskWithoutExecution()
    {
        var executor = Substitute.For<ITaskExecutor>();
        await using var fx = await DesktopAuthorityFixture.CreateAsync("""{"steps":[{"tool":"launch_app","target":"notepad"}]}""", executor);
        var bridge = new FakeUiBridge(_ => new AuthorityApprovalDecision(false, null));
        var service = fx.CreateService(bridge);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => service.ConnectionStatus.Contains("Hosting", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(10));
            await using var cli = new NamedPipeTaskAuthorityClient(fx.PipeName, "cli-test", canApprove: false, connectTimeout: TimeSpan.FromSeconds(5));
            await cli.EnsureConnectedAsync();
            var submit = await cli.SendAsync(new AuthorityRequest("s1", AuthorityRequestKind.SubmitTask, Goal: "deny me"));
            var taskId = submit.Task!.TaskId;
            var terminal = await WaitTerminalViaClientAsync(cli, taskId, TimeSpan.FromSeconds(20));
            Assert.NotNull(terminal);
            Assert.NotEqual("Completed", terminal!.Phase);
            await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HostingDesktopService_EmergencyStopCancelsPendingApprovalWithoutExecution()
    {
        var executor = Substitute.For<ITaskExecutor>();
        await using var fx = await DesktopAuthorityFixture.CreateAsync("""{"steps":[{"tool":"launch_app","target":"notepad"}]}""", executor);
        var bridge = new BlockingUiBridge();
        var service = fx.CreateService(bridge);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => service.ConnectionStatus.Contains("Hosting", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(10));
            await using var cli = new NamedPipeTaskAuthorityClient(fx.PipeName, "cli-stop-test", canApprove: false, connectTimeout: TimeSpan.FromSeconds(5));
            await cli.EnsureConnectedAsync();
            var submit = await cli.SendAsync(new AuthorityRequest("stop-submit", AuthorityRequestKind.SubmitTask, Goal: "stop while approval pending"));
            Assert.True(submit.Ok);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await bridge.PromptShown.Task.WaitAsync(timeout.Token);
            var stop = await cli.SendAsync(new AuthorityRequest("stop-request", AuthorityRequestKind.Stop), timeout.Token);
            Assert.True(stop.Ok);
            await bridge.PromptCancelled.Task.WaitAsync(timeout.Token);
            // A cancelled dialog must never result in dispatch, even if the UI later tries to approve.
            bridge.ReturnLateApproval();
            await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition not met.");
    }

    private static async Task<TaskSnapshot?> WaitTerminalViaClientAsync(NamedPipeTaskAuthorityClient client, Guid taskId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var evt in client.Events(cts.Token))
        {
            if (evt.Task?.TaskId == taskId && evt.Task.Phase is "Completed" or "Failed" or "Cancelled")
                return evt.Task;
            if (evt.Kind == AuthorityEventKind.TaskCompleted && evt.Task?.TaskId == taskId)
                return evt.Task;
            if (evt.State?.ActiveTask is null)
            {
                var state = await client.SendAsync(new AuthorityRequest("poll", AuthorityRequestKind.GetTask, TaskId: taskId), cts.Token);
                if (state.Task?.Phase is "Completed" or "Failed" or "Cancelled") return state.Task;
            }
        }
        return null;
    }

    private sealed class BlockingUiBridge : ITaskAuthorityUiBridge
    {
        private readonly TaskCompletionSource<AuthorityApprovalDecision?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> PromptShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> PromptCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AuthorityApprovalDecision?> RequestApprovalAsync(ApprovalRequestSnapshot request, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => PromptCancelled.TrySetResult(true));
            PromptShown.TrySetResult(true);
            return _result.Task;
        }

        public void ReturnLateApproval() => _result.TrySetResult(new AuthorityApprovalDecision(true, null));
    }

    private sealed class FakeUiBridge : ITaskAuthorityUiBridge
    {
        private readonly Func<ApprovalRequestSnapshot, AuthorityApprovalDecision?> _decide;
        public ApprovalRequestSnapshot? LastRequest { get; private set; }
        public FakeUiBridge(Func<ApprovalRequestSnapshot, AuthorityApprovalDecision?> decide) => _decide = decide;
        public Task<AuthorityApprovalDecision?> RequestApprovalAsync(ApprovalRequestSnapshot request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_decide(request));
        }
    }

    private sealed class DesktopAuthorityFixture : IAsyncDisposable
    {
        public required string PipeName { get; init; }
        public required ICapriPaths Paths { get; init; }
        public required ITaskHistoryStore History { get; init; }
        public required IChatProvider Provider { get; init; }
        public required IModelRouter Router { get; init; }
        public required ITaskExecutor Executor { get; init; }
        public required ITaskJournal Journal { get; init; }
        public required ITaskControl Control { get; init; }
        public required TempDirectory Temp { get; init; }

        public static Task<DesktopAuthorityFixture> CreateAsync(string planJson, ITaskExecutor? executor = null)
        {
            var temp = new TempDirectory();
            var paths = new CapriPaths(temp.Path);
            var history = new FileTaskHistoryStore(paths);
            var control = new FileTaskControl(paths);
            var journal = new TaskMetadataJournal(paths, history);
            var provider = new StubChatProvider { ProviderId = "ollama", CompletionResult = new ChatCompletion(new ChatMessage(ChatRole.Assistant, planJson)) };
            var router = new StubModelRouter("ollama", "stub-model", supportsStreaming: false);
            var exec = executor ?? Substitute.For<ITaskExecutor>();
            if (executor is null)
                exec.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new TaskOutcome(true, false, "dispatched"));
            var pipeName = "capri-test-dt-" + Guid.NewGuid().ToString("N");
            return Task.FromResult(new DesktopAuthorityFixture { PipeName = pipeName, Paths = paths, History = history, Provider = provider, Router = router, Executor = exec, Journal = journal, Control = control, Temp = temp });
        }

        public DesktopTaskAuthorityService CreateService(ITaskAuthorityUiBridge bridge) =>
            new(Paths, History, Provider, Router, Executor, Journal, Control, bridge, NullLogger<DesktopTaskAuthorityService>.Instance, PipeName);

        public ValueTask DisposeAsync() { Temp.Dispose(); return ValueTask.CompletedTask; }
    }
}
