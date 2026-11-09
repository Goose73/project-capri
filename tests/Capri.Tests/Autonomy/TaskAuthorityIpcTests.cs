using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;
using Capri.Core.Models;
using Capri.Core.Providers;
using Capri.Security;
using Capri.Security.Autonomy;
using Capri.Security.Autonomy.Authority;
using Capri.Tests.Cli;
using Capri.Tests.TestDoubles;
using NSubstitute;

namespace Capri.Tests.Autonomy;

public sealed class TaskAuthorityIpcTests
{
    [Fact]
    public async Task InProcessSubmitApproveAndCompleteThroughAuthority()
    {
        await using var fx = await AuthorityFixture.CreateAsync(planJson: """{"steps":[{"tool":"launch_app","target":"notepad"}]}""");
        await using var client = fx.CreateClient("test");
        await client.EnsureConnectedAsync();

        var submit = await client.SendAsync(Req(AuthorityRequestKind.SubmitTask, goal: "open notepad"));
        Assert.True(submit.Ok);
        var taskId = submit.Task!.TaskId;

        var completed = await ApproveAndWaitAsync(client, taskId, workspace: null);
        Assert.Equal("Completed", completed);

        var list = await client.SendAsync(Req(AuthorityRequestKind.ListTasks, maxCount: 10));
        Assert.True(list.Ok);
        Assert.Contains(list.Tasks ?? [], t => t.TaskId == taskId);
    }

    [Fact]
    public async Task ApprovalRejectsWrongPlanHashThenAcceptsCorrectHash()
    {
        await using var fx = await AuthorityFixture.CreateAsync(planJson: """{"steps":[{"tool":"launch_app","target":"calculator"}]}""");
        await using var client = fx.CreateClient("test");
        await client.EnsureConnectedAsync();
        var submit = await client.SendAsync(Req(AuthorityRequestKind.SubmitTask, goal: "open calculator"));
        var taskId = submit.Task!.TaskId;

        ApprovalRequestSnapshot? approval = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await foreach (var evt in client.Events(cts.Token))
        {
            if (evt.Approval?.TaskId == taskId) { approval = evt.Approval; break; }
        }
        Assert.NotNull(approval);

        var bad = await client.SendAsync(new AuthorityRequest("bad", AuthorityRequestKind.Approve, TaskId: taskId, ApprovalId: approval!.ApprovalId, PlanHash: "deadbeef"), cts.Token);
        Assert.False(bad.Ok);
        Assert.Contains("hash", bad.Error ?? "", StringComparison.OrdinalIgnoreCase);

        // Replay with same approval id but after denial? First deny correctly to finish.
        var deny = await client.SendAsync(new AuthorityRequest("deny", AuthorityRequestKind.Deny, TaskId: taskId, ApprovalId: approval.ApprovalId, PlanHash: approval.PlanHash), cts.Token);
        Assert.True(deny.Ok);

        // Attempting to approve again must not replay/succeed.
        var replay = await client.SendAsync(new AuthorityRequest("replay", AuthorityRequestKind.Approve, TaskId: taskId, ApprovalId: approval.ApprovalId, PlanHash: approval.PlanHash), cts.Token);
        Assert.False(replay.Ok);

        var final = await WaitTerminalAsync(client, taskId, cts.Token);
        Assert.Equal("Cancelled", final);
    }

    [Fact]
    public async Task ConcurrentSecondSubmitRejectedWhileTaskActive()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Capri.Core.Autonomy.TaskOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async call => { entered.TrySetResult(); var ct = call.Arg<CancellationToken>(); return await release.Task.WaitAsync(ct); });

        await using var fx = await AuthorityFixture.CreateAsync(planJson: """{"steps":[{"tool":"launch_app","target":"notepad"}]}""", executor: executor);
        await using var c1 = fx.CreateClient("c1");
        await using var c2 = fx.CreateClient("c2");
        await c1.EnsureConnectedAsync(); await c2.EnsureConnectedAsync();

        var s1 = await c1.SendAsync(Req(AuthorityRequestKind.SubmitTask, goal: "task 1"));
        Assert.True(s1.Ok);
        await ApproveWhenRequestedAsync(c1, s1.Task!.TaskId, null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var s2 = await c2.SendAsync(Req(AuthorityRequestKind.SubmitTask, goal: "task 2"));
        Assert.False(s2.Ok);
        Assert.Contains("active", s2.Error ?? "", StringComparison.OrdinalIgnoreCase);

        release.TrySetResult(new TaskOutcome(true, false, "dispatched"));
        var final = await WaitTerminalAsync(c1, s1.Task.TaskId, CancellationToken.None);
        Assert.Equal("Completed", final);
    }

    [Fact]
    public async Task StopCancelsActiveTaskThroughAuthority()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<Capri.Core.Autonomy.TaskOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async call => { entered.TrySetResult(); var ct = call.Arg<CancellationToken>(); return await never.Task.WaitAsync(ct); });

        await using var fx = await AuthorityFixture.CreateAsync(planJson: """{"steps":[{"tool":"launch_app","target":"notepad"}]}""", executor: executor);
        await using var c1 = fx.CreateClient("c1");
        await using var c2 = fx.CreateClient("c2");
        await c1.EnsureConnectedAsync(); await c2.EnsureConnectedAsync();

        var s1 = await c1.SendAsync(Req(AuthorityRequestKind.SubmitTask, goal: "task"));
        await ApproveWhenRequestedAsync(c1, s1.Task!.TaskId, null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stop = await c2.SendAsync(Req(AuthorityRequestKind.Stop));
        Assert.True(stop.Ok);
        var final = await WaitTerminalAsync(c1, s1.Task.TaskId, CancellationToken.None);
        Assert.Equal("Cancelled", final);
    }

    [Fact]
    public async Task SubprocessClientCanGetStateAndSubmitApprove()
    {
        await using var fx = await AuthorityFixture.CreateAsync(planJson: """{"steps":[{"tool":"launch_app","target":"notepad"}]}""");
        var probe = AuthorityProbePath();
        Assert.True(File.Exists(probe), $"Authority probe not found at {probe}");

        var state = await RunProbeAsync(probe, fx.PipeName, "get-state");
        Assert.Equal(0, state.exit);
        Assert.Contains("\"ok\":true", state.stdout, StringComparison.OrdinalIgnoreCase);

        var submit = await RunProbeAsync(probe, fx.PipeName, "submit-approve-wait", "open notepad from probe");
        Assert.Equal(0, submit.exit);
        Assert.Contains("\"result\":\"Completed\"", submit.stdout.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubprocessClientApprovalBindsToExactPlanHash()
    {
        await using var fx = await AuthorityFixture.CreateAsync(planJson: """{"steps":[{"tool":"launch_app","target":"notepad"}]}""");
        var probe = AuthorityProbePath();
        var result = await RunProbeAsync(probe, fx.PipeName, "submit-bad-hash", "hash test");
        Assert.Equal(0, result.exit);
        Assert.Contains("\"badHashOk\":false", result.stdout.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"denyOk\":true", result.stdout.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaleApprovalRejectedAfterTaskCancelled()
    {
        await using var fx = await AuthorityFixture.CreateAsync(planJson: """{"steps":[{"tool":"launch_app","target":"notepad"}]}""");
        await using var client = fx.CreateClient("stale");
        await client.EnsureConnectedAsync();
        var submit = await client.SendAsync(Req(AuthorityRequestKind.SubmitTask, goal: "stale approval test"));
        Assert.True(submit.Ok);
        var taskId = submit.Task!.TaskId;

        ApprovalRequestSnapshot? approval = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await foreach (var evt in client.Events(cts.Token))
        {
            if (evt.Approval?.TaskId == taskId) { approval = evt.Approval; break; }
        }
        Assert.NotNull(approval);

        var cancel = await client.SendAsync(new AuthorityRequest("cancel", AuthorityRequestKind.CancelTask, TaskId: taskId), cts.Token);
        Assert.True(cancel.Ok);
        await WaitTerminalAsync(client, taskId, cts.Token);

        var stale = await client.SendAsync(new AuthorityRequest("stale", AuthorityRequestKind.Approve,
            TaskId: taskId, ApprovalId: approval!.ApprovalId, PlanHash: approval.PlanHash), cts.Token);
        Assert.False(stale.Ok);
    }

    [Fact]
    public async Task NoExecutionWithoutExplicitExactPlanApproval()
    {
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TaskOutcome(true, false, "should not run"));

        await using var fx = await AuthorityFixture.CreateAsync(planJson: """{"steps":[{"tool":"launch_app","target":"notepad"}]}""", executor: executor);
        await using var client = fx.CreateClient("noexec");
        await client.EnsureConnectedAsync();
        var submit = await client.SendAsync(Req(AuthorityRequestKind.SubmitTask, goal: "no exec"));
        var taskId = submit.Task!.TaskId;

        ApprovalRequestSnapshot? approval = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await foreach (var evt in client.Events(cts.Token))
        {
            if (evt.Approval?.TaskId == taskId) { approval = evt.Approval; break; }
        }
        Assert.NotNull(approval);

        var bad = await client.SendAsync(new AuthorityRequest("bad", AuthorityRequestKind.Approve, TaskId: taskId, ApprovalId: approval!.ApprovalId, PlanHash: "mismatch"), cts.Token);
        Assert.False(bad.Ok);
        var deny = await client.SendAsync(new AuthorityRequest("deny", AuthorityRequestKind.Deny, TaskId: taskId, ApprovalId: approval.ApprovalId, PlanHash: approval.PlanHash), cts.Token);
        Assert.True(deny.Ok);
        await WaitTerminalAsync(client, taskId, cts.Token);

        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    private static AuthorityRequest Req(AuthorityRequestKind kind, string? goal = null, int? maxCount = null)
        => new(Guid.NewGuid().ToString("N"), kind, Goal: goal, MaxCount: maxCount);

    private static async Task<string> ApproveAndWaitAsync(NamedPipeTaskAuthorityClient client, Guid taskId, string? workspace)
    {
        await ApproveWhenRequestedAsync(client, taskId, workspace);
        return await WaitTerminalAsync(client, taskId, CancellationToken.None);
    }

    private static async Task ApproveWhenRequestedAsync(NamedPipeTaskAuthorityClient client, Guid taskId, string? workspace)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await foreach (var evt in client.Events(cts.Token))
        {
            if (evt.Approval?.TaskId == taskId)
            {
                var a = evt.Approval;
                var resp = await client.SendAsync(new AuthorityRequest("ap", AuthorityRequestKind.Approve, TaskId: taskId, ApprovalId: a.ApprovalId, PlanHash: a.PlanHash, Workspace: workspace), cts.Token);
                Assert.True(resp.Ok, resp.Error);
                return;
            }
        }
        throw new TimeoutException("No approval request observed.");
    }

    private static async Task<string> WaitTerminalAsync(NamedPipeTaskAuthorityClient client, Guid taskId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        string? last = null;
        await foreach (var evt in client.Events(cts.Token))
        {
            if (evt.Progress?.TaskId == taskId || evt.Progress?.TaskId is null) last = evt.Progress?.Phase.ToString() ?? last;
            if (evt.Kind == AuthorityEventKind.TaskCompleted) return evt.Task?.Phase ?? last ?? "completed";
            if (last is "Completed" or "Failed" or "Cancelled") return last;
        }
        return last ?? "timeout";
    }

    private static string AuthorityProbePath()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "AuthorityProbe");
        var exe = Path.Combine(dir, "Capri.AuthorityProbe.exe");
        if (File.Exists(exe)) return exe;
        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "Capri.AuthorityProbe", "bin", "Release", "net10.0-windows", "Capri.AuthorityProbe.exe");
    }

    private static async Task<(int exit, string stdout, string stderr)> RunProbeAsync(string probe, params string[] args)
    {
        var psi = new ProcessStartInfo(probe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("probe did not start");
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        return (p.ExitCode, stdout, stderr);
    }

    private sealed class AuthorityFixture : IAsyncDisposable
    {
        public required string PipeName { get; init; }
        public required NamedPipeTaskAuthorityServer Server { get; init; }
        public required TempDirectory Temp { get; init; }

        public static async Task<AuthorityFixture> CreateAsync(string planJson, ITaskExecutor? executor = null)
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
            var pipeName = "capri-test-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeTaskAuthorityServer(pipeName, paths, history, provider, router, exec, journal, control);
            server.Start();
            // give server a moment
            await Task.Delay(50);
            return new AuthorityFixture { PipeName = pipeName, Server = server, Temp = temp };
        }

        public NamedPipeTaskAuthorityClient CreateClient(string name) => new(PipeName, name, canApprove: true, connectTimeout: TimeSpan.FromSeconds(3));

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            Temp.Dispose();
        }
    }
}
