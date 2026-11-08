using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Capri.Automation.Autonomy;
using Capri.Core.Abstractions;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Autonomy;
using Capri.Core.Models;
using Capri.Tests.TestDoubles;
using Capri.Security;
using System.Text.Json;
using NSubstitute;

namespace Capri.Tests.Autonomy;

public sealed class TaskSliceTests
{
    [Fact]
    public async Task ConcurrentJournalInstancesPublishOnlyCompleteMetadata()
    {
        using var workspace = new TempDirectory();
        var paths = new CapriPaths(workspace.Path);
        var writers = Enumerable.Range(0, 4).Select(_ => new TaskMetadataJournal(paths)).ToArray();
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => writers[i % 4].Record(Guid.NewGuid(), TaskPhase.Executing, i))));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workspace.Path, "task-latest.json")));
        Assert.True(json.RootElement.TryGetProperty("stepIndex", out _));
        Assert.Empty(Directory.GetFiles(workspace.Path, "*.tmp"));
        Assert.DoesNotContain("text", json.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SlowDispatchCancellationDoesNotAdvanceOrRepeat()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<TaskOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.TrySetResult(); return never.Task; });
        var coordinator = CreateCoordinator("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"},{\"tool\":\"launch_app\",\"target\":\"paint\"}]}", executor, "");
        using var cts = new CancellationTokenSource();
        var running = Collect(coordinator.RunAsync("launch", cts.Token));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        Assert.Equal(TaskPhase.Cancelled, (await running.WaitAsync(TimeSpan.FromSeconds(5)))[^1].Phase);
        await executor.Received(1).ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        never.TrySetResult(new TaskOutcome(true, true, "late"));
    }

    [Fact]
    public async Task LateApprovalAfterCancellationCannotDispatch()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decision = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion(new ChatMessage(ChatRole.Assistant, "{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}")));
        var router = Substitute.For<IModelRouter>();
        router.ResolveAsync(Arg.Any<ModelRouteRequest>(), Arg.Any<CancellationToken>()).Returns(new ModelRoute("fake", "fake"));
        var approval = Substitute.For<ITaskApproval>();
        approval.ApproveAsync(Arg.Any<TaskPlan>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.TrySetResult(); return decision.Task; });
        var executor = Substitute.For<ITaskExecutor>();
        var coordinator = new TaskCoordinator(provider, router, approval, executor, Substitute.For<ITaskJournal>());
        using var cts = new CancellationTokenSource();
        var running = Collect(coordinator.RunAsync("launch", cts.Token));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        decision.SetResult(string.Empty);
        Assert.Equal(TaskPhase.Cancelled, (await running.WaitAsync(TimeSpan.FromSeconds(5)))[^1].Phase);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [Fact]
    public async Task ConcurrentNoteCreationNeverOverwritesFirstWriter()
    {
        using var workspace = new TempDirectory();
        var executor = new SafeTaskExecutor(Substitute.For<IAppAliasResolver>(), Substitute.For<IAppLauncher>(), Substitute.For<IWebLauncher>());
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            try { await executor.ExecuteAsync(new TaskStep(TaskTool.CreateNote, "one.txt", i.ToString(System.Globalization.CultureInfo.InvariantCulture)), workspace.Path, CancellationToken.None); return true; }
            catch (IOException) { return false; }
        }));
        Assert.Single(results, x => x);
        Assert.Single(await File.ReadAllTextAsync(Path.Combine(workspace.Path, "one.txt")));
    }

    [Fact]
    public async Task MalformedFirstPlanIsRepairedExactlyOnceBeforeApproval()
    {
        const string validPlan = "{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}";
        var responses = new Queue<string>(["not json", validPlan]);
        var order = new List<string>();
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("planner");
                return Task.FromResult(new ChatCompletion(
                    new ChatMessage(ChatRole.Assistant, responses.Dequeue())));
            });
        var router = Substitute.For<IModelRouter>();
        router.ResolveAsync(Arg.Any<ModelRouteRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelRoute("fake", "fake"));
        var approval = Substitute.For<ITaskApproval>();
        approval.ApproveAsync(Arg.Any<TaskPlan>(), Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("approve"); return Task.FromResult<string?>(""); });
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("execute");
                return Task.FromResult(new TaskOutcome(true, true, "ok"));
            });
        var coordinator = new TaskCoordinator(
            provider, router, approval, executor, Substitute.For<ITaskJournal>());

        var events = await Collect(coordinator.RunAsync("launch notepad"));

        Assert.Equal(TaskPhase.Completed, events[^1].Phase);
        Assert.Equal(4, order.Count);
        Assert.Equal("planner", order[0]);
        Assert.Equal("planner", order[1]);
        Assert.Equal("approve", order[2]);
        Assert.Equal("execute", order[3]);
        Assert.Contains(events, e => e.Phase == TaskPhase.Planning
            && e.Message.Contains("repair", StringComparison.OrdinalIgnoreCase));
        await provider.Received(2).CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
        await approval.Received(1).ApproveAsync(Arg.Any<TaskPlan>(), Arg.Any<CancellationToken>());
        await executor.Received(1).ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MalformedInitialAndRepairResponsesFailWithoutApprovalOrDispatch()
    {
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatCompletion(
                new ChatMessage(ChatRole.Assistant, "not json"))));
        var router = Substitute.For<IModelRouter>();
        router.ResolveAsync(Arg.Any<ModelRouteRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelRoute("fake", "fake"));
        var approval = Substitute.For<ITaskApproval>();
        var executor = Substitute.For<ITaskExecutor>();
        var coordinator = new TaskCoordinator(
            provider, router, approval, executor, Substitute.For<ITaskJournal>());

        var events = await Collect(coordinator.RunAsync("launch notepad"));

        Assert.Equal(TaskPhase.Failed, events[^1].Phase);
        Assert.Contains(events, e => e.Phase == TaskPhase.Planning
            && e.Message.Contains("repair", StringComparison.OrdinalIgnoreCase));
        await provider.Received(2).CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
        await approval.DidNotReceiveWithAnyArgs().ApproveAsync(default!, default);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [Fact]
    public async Task RepairNeverHappensAfterApprovalOrDispatch()
    {
        const string validPlan = "{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}";
        var order = new List<string>();
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("planner");
                return Task.FromResult(new ChatCompletion(
                    new ChatMessage(ChatRole.Assistant, validPlan)));
            });
        var router = Substitute.For<IModelRouter>();
        router.ResolveAsync(Arg.Any<ModelRouteRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelRoute("fake", "fake"));
        var approval = Substitute.For<ITaskApproval>();
        approval.ApproveAsync(Arg.Any<TaskPlan>(), Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("approve"); return Task.FromResult<string?>(""); });
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("execute");
                // Force a post-dispatch failure: repair must still never run.
                return Task.FromResult(new TaskOutcome(false, false, "failed"));
            });
        var coordinator = new TaskCoordinator(
            provider, router, approval, executor, Substitute.For<ITaskJournal>());

        var events = await Collect(coordinator.RunAsync("launch notepad"));

        Assert.Equal(TaskPhase.Failed, events[^1].Phase);
        Assert.Equal(3, order.Count);
        Assert.Equal("planner", order[0]);
        Assert.Equal("approve", order[1]);
        Assert.Equal("execute", order[2]);
        Assert.DoesNotContain(events, e => e.Message.Contains("repair", StringComparison.OrdinalIgnoreCase));
        await provider.Received(1).CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
        await approval.Received(1).ApproveAsync(Arg.Any<TaskPlan>(), Arg.Any<CancellationToken>());
        await executor.Received(1).ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MalformedPlannerResponseDoesNotRequestApproval()
    {
        var approval = Substitute.For<ITaskApproval>();
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion(new ChatMessage(ChatRole.Assistant, "not json")));
        var router = Substitute.For<IModelRouter>();
        router.ResolveAsync(Arg.Any<ModelRouteRequest>(), Arg.Any<CancellationToken>()).Returns(new ModelRoute("fake", "fake"));
        var executor = Substitute.For<ITaskExecutor>();
        var coordinator = new TaskCoordinator(provider, router, approval, executor, Substitute.For<ITaskJournal>());
        Assert.Equal(TaskPhase.Failed, (await Collect(coordinator.RunAsync("goal")))[^1].Phase);
        await approval.DidNotReceiveWithAnyArgs().ApproveAsync(default!, default);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"steps\":[{\"tool\":\"run_command\",\"target\":\"dir\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"create_note\",\"target\":\"../escape.txt\",\"text\":\"x\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"open_url\",\"target\":\"file:///C:/Windows\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"open_note\",\"target\":\"a.txt\"}]}")]
    public void RejectsUntrustedPlans(string json) => Assert.ThrowsAny<Exception>(() => TaskPlanParser.Parse(json));

    [Fact]
    public async Task DenialPerformsZeroActions()
    {
        var executor = Substitute.For<ITaskExecutor>();
        var coordinator = CreateCoordinator("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}", executor, null);
        var events = await Collect(coordinator.RunAsync("launch notepad"));
        Assert.Equal(TaskPhase.Cancelled, events[^1].Phase);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [Fact]
    public async Task StopLatchBlocksDispatchUntilExplicitReset()
    {
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TaskOutcome(true, false, "dispatched"));
        var coordinator = CreateCoordinator("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}", executor, "");
        coordinator.Stop();
        Assert.Equal(TaskPhase.Cancelled, (await Collect(coordinator.RunAsync("launch")))[^1].Phase);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        coordinator.ResetStop();
        Assert.Equal(TaskPhase.Completed, (await Collect(coordinator.RunAsync("launch")))[^1].Phase);
        await executor.Received(1).ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellationAndFailureDoNotReplay()
    {
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TaskOutcome(false, false, "not verified"));
        var coordinator = CreateCoordinator("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"},{\"tool\":\"launch_app\",\"target\":\"paint\"}]}", executor, "");
        Assert.Equal(TaskPhase.Failed, (await Collect(coordinator.RunAsync("launch")))[^1].Phase);
        await executor.Received(1).ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Equal(TaskPhase.Cancelled, (await Collect(coordinator.RunAsync("launch", cts.Token)))[^1].Phase);
        await executor.Received(1).ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetCannotReviveAnAlreadyStoppedTask()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<TaskOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.SetResult(); return release.Task; });
        var coordinator = CreateCoordinator("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"},{\"tool\":\"launch_app\",\"target\":\"paint\"}]}", executor, "");
        var running = Collect(coordinator.RunAsync("launch"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Stop();
        coordinator.ResetStop();
        release.SetResult(new TaskOutcome(true, false, "dispatched"));
        Assert.Equal(TaskPhase.Cancelled, (await running)[^1].Phase);
        await executor.Received(1).ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RealNoteExecutorCreatesAndVerifiesButWillNotOverwrite()
    {
        using var workspace = new TempDirectory();
        var executor = new SafeTaskExecutor(Substitute.For<IAppAliasResolver>(), Substitute.For<IAppLauncher>(), Substitute.For<IWebLauncher>());
        var action = new TaskStep(TaskTool.CreateNote, "new.txt", "literal text");
        var result = await executor.ExecuteAsync(action, workspace.Path, CancellationToken.None);
        Assert.True(result.Verified);
        Assert.Equal("literal text", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "new.txt")));
        await Assert.ThrowsAsync<IOException>(() => executor.ExecuteAsync(action, workspace.Path, CancellationToken.None));
        Assert.Equal("literal text", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "new.txt")));
    }

    [Fact]
    public async Task OpenNoteRealWorkspaceWithUnavailableNotepadFailsWithoutProcessLaunch()
    {
        using var workspace = new TempDirectory();
        var note = Path.Combine(workspace.Path, "plan.txt");
        await File.WriteAllTextAsync(note, "hello");
        var aliases = Substitute.For<IAppAliasResolver>();
        aliases.ResolveAsync("notepad", Arg.Any<CancellationToken>()).Returns((AppTarget?)null);
        var executor = new SafeTaskExecutor(aliases, Substitute.For<IAppLauncher>(), Substitute.For<IWebLauncher>());

        var result = await executor.ExecuteAsync(new TaskStep(TaskTool.OpenNote, "plan.txt"), workspace.Path, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Verified);
        Assert.Contains("Notepad unavailable", result.Message, StringComparison.Ordinal);
        Assert.Equal("hello", await File.ReadAllTextAsync(note));
    }

    [Fact]
    public async Task CreateNoteRequiresApprovedWorkspace()
    {
        var executor = new SafeTaskExecutor(Substitute.For<IAppAliasResolver>(), Substitute.For<IAppLauncher>(), Substitute.For<IWebLauncher>());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(new TaskStep(TaskTool.CreateNote, "x.txt", "t"), workspace: null, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"steps\":[]}")]
    [InlineData("{\"steps\":[{\"tool\":\"unknown\",\"target\":\"x\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"create_note\",\"target\":\"x.txt\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"create_note\",\"target\":\"x.txt\",\"text\":\"a\",\"extra\":\"b\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"open_url\",\"target\":\"http://example.com\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"open_url\",\"target\":\"https://user@example.com/path\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"open_url\",\"target\":\"https://example.com/has space\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"cmd\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"create_note\",\"target\":\"sub/x.txt\",\"text\":\"x\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"create_note\",\"target\":\".txt\",\"text\":\"x\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"create_note\",\"target\":\"dup.txt\",\"text\":\"a\"},{\"tool\":\"create_note\",\"target\":\"dup.txt\",\"text\":\"b\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"open_note\",\"target\":\"missing.txt\"},{\"tool\":\"create_note\",\"target\":\"missing.txt\",\"text\":\"x\"}]}")]
    public void RejectsMalformedOrUnsafePlans(string json) => Assert.ThrowsAny<Exception>(() => TaskPlanParser.Parse(json));

    [Fact]
    public void ParsesMinimalSafePlanAndOpenNoteRequiresPriorCreate()
    {
        var plan = TaskPlanParser.Parse("{\"steps\":[{\"tool\":\"create_note\",\"target\":\"a.txt\",\"text\":\"hi\"},{\"tool\":\"open_note\",\"target\":\"a.txt\"}]}");
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(TaskTool.CreateNote, plan.Steps[0].Tool);
        Assert.Equal(TaskTool.OpenNote, plan.Steps[1].Tool);
    }

    [Fact]
    public async Task DenialOfNotePlanPerformsZeroActionsAndCompletesCancelled()
    {
        var executor = Substitute.For<ITaskExecutor>();
        var coordinator = CreateCoordinator("{\"steps\":[{\"tool\":\"create_note\",\"target\":\"n.txt\",\"text\":\"x\"}]}", executor, workspace: null);
        var events = await Collect(coordinator.RunAsync("make note"));
        Assert.Equal(TaskPhase.Cancelled, events[^1].Phase);
        Assert.Contains(events, e => e.Phase == TaskPhase.AwaitingPermission);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [Fact]
    public async Task CancellationBeforeApprovalDispatchesNoSteps()
    {
        var enteredApproval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion(new ChatMessage(ChatRole.Assistant, "{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}")));
        var router = Substitute.For<IModelRouter>();
        router.ResolveAsync(Arg.Any<ModelRouteRequest>(), Arg.Any<CancellationToken>()).Returns(new ModelRoute("fake", "fake"));
        var executor = Substitute.For<ITaskExecutor>();
        var approval = Substitute.For<ITaskApproval>();
        using var cts = new CancellationTokenSource();
        approval.ApproveAsync(Arg.Any<TaskPlan>(), Arg.Any<CancellationToken>())
            .Returns(call => WaitForCancellationAsync(enteredApproval, call.Arg<CancellationToken>()));

        var coordinator = new TaskCoordinator(provider, router, approval, executor, Substitute.For<ITaskJournal>());
        var running = Collect(coordinator.RunAsync("launch", cts.Token));
        await enteredApproval.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        var events = await running;
        Assert.Equal(TaskPhase.Cancelled, events[^1].Phase);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [Fact]
    public async Task JournalRecordsInFlightStepBeforeDispatchAndMetadataPhases()
    {
        var records = new List<(Guid Id, TaskPhase Phase, int Step)>();
        var journal = Substitute.For<ITaskJournal>();
        journal.When(j => j.Record(Arg.Any<Guid>(), Arg.Any<TaskPhase>(), Arg.Any<int>()))
            .Do(call => records.Add((call.Arg<Guid>(), call.Arg<TaskPhase>(), call.Arg<int>())));
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.Contains(records, r => r.Phase == TaskPhase.Executing && r.Step == 0);
                return new TaskOutcome(true, false, "dispatched");
            });

        var coordinator = CreateCoordinatorWithJournal("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}", executor, "", journal);
        var events = await Collect(coordinator.RunAsync("launch"));

        Assert.Equal(TaskPhase.Completed, events[^1].Phase);
        Assert.Contains(records, r => r.Phase == TaskPhase.Created);
        Assert.Contains(records, r => r.Phase == TaskPhase.Planning);
        Assert.Contains(records, r => r.Phase == TaskPhase.AwaitingPermission);
        Assert.Contains(records, r => r.Phase == TaskPhase.Observing);
        Assert.Contains(records, r => r.Phase == TaskPhase.Verifying);
        Assert.Contains(records, r => r.Phase == TaskPhase.Completed);
        Assert.True(records.Count(r => r.Phase == TaskPhase.Executing) == 1);
    }

    private static TaskCoordinator CreateCoordinatorWithJournal(string response, ITaskExecutor executor, string? workspace, ITaskJournal journal)
    {
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion(new ChatMessage(ChatRole.Assistant, response)));
        var router = Substitute.For<IModelRouter>();
        router.ResolveAsync(Arg.Any<ModelRouteRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelRoute("fake", "fake"));
        var approval = Substitute.For<ITaskApproval>();
        approval.ApproveAsync(Arg.Any<TaskPlan>(), Arg.Any<CancellationToken>()).Returns(workspace);
        return new TaskCoordinator(provider, router, approval, executor, journal);
    }

    private static TaskCoordinator CreateCoordinator(string response, ITaskExecutor executor, string? workspace)
    {
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion(new ChatMessage(ChatRole.Assistant, response)));
        var router = Substitute.For<IModelRouter>();
        router.ResolveAsync(Arg.Any<ModelRouteRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelRoute("fake", "fake"));
        var approval = Substitute.For<ITaskApproval>();
        approval.ApproveAsync(Arg.Any<TaskPlan>(), Arg.Any<CancellationToken>()).Returns(workspace);
        var journal = Substitute.For<ITaskJournal>();
        return new TaskCoordinator(provider, router, approval, executor, journal);
    }

    private static async Task<List<TaskProgress>> Collect(IAsyncEnumerable<TaskProgress> source)
    {
        var list = new List<TaskProgress>();
        await foreach (var item in source) list.Add(item);
        return list;
    }

    private static async Task<string?> WaitForCancellationAsync(TaskCompletionSource entered, CancellationToken cancellationToken)
    {
        entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return null;
    }
}
