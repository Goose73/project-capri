using System.Net.Http;
using Capri.Cli;
using Capri.Cli.Commands;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Core.Models;
using Capri.Core.Providers;
using Capri.Security;
using Capri.Security.Autonomy;
using Capri.Tests.TestDoubles;
using NSubstitute;

namespace Capri.Tests.Cli;

public sealed class TaskCommandTests
{
    [Fact]
    public async Task TaskRunRefusesNonInteractiveTerminal()
    {
        using var temp = new TempDirectory();
        var services = CreateServices(temp.Path);
        var console = new FakeCliConsole(interactive: false);
        var command = new TaskCommand(console, services);
        var code = await command.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["open notepad"] });
        Assert.Equal(CliExitCodes.Usage, code);
        Assert.Contains("interactive terminal", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskRunDeniedApprovalDispatchesNothing()
    {
        using var temp = new TempDirectory();
        var executor = Substitute.For<ITaskExecutor>();
        var services = CreateServices(temp.Path, executor: executor,
            providerResult: "{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}");
        var console = new FakeCliConsole(["deny"], interactive: true);
        var command = new TaskCommand(console, services);

        var code = await command.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["open notepad"] });

        Assert.Equal(CliExitCodes.Error, code);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        Assert.Contains("Denied", console.Out + console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskRunEofApprovalRejects()
    {
        using var temp = new TempDirectory();
        var executor = Substitute.For<ITaskExecutor>();
        var services = CreateServices(temp.Path, executor: executor,
            providerResult: "{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}");
        var console = new FakeCliConsole(Array.Empty<string?>(), interactive: true); // EOF
        var command = new TaskCommand(console, services);

        var code = await command.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["open notepad"] });

        Assert.Equal(CliExitCodes.Error, code);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [Fact]
    public async Task TaskRunGrantedApprovalExecutesViaCoordinator()
    {
        using var temp = new TempDirectory();
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TaskOutcome(true, false, "Launch dispatched: Notepad; window not verified."));
        var services = CreateServices(temp.Path, executor: executor,
            providerResult: "{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}");
        var console = new FakeCliConsole(["approve PLAN"], interactive: true);
        var command = new TaskCommand(console, services);

        var code = await command.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["open notepad"] });

        Assert.Equal(CliExitCodes.Success, code);
        await executor.Received(1).ExecuteAsync(Arg.Is<TaskStep>(s => s.Tool == TaskTool.LaunchApp && s.Target == "notepad"), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.Contains("ollama", console.Out, StringComparison.OrdinalIgnoreCase); // provider/model shown
        Assert.Contains("Dispatched only", console.Out, StringComparison.OrdinalIgnoreCase); // dispatch-vs-verification reporting
    }

    [Fact]
    public async Task TaskRunMalformedPlanFailsWithoutDispatch()
    {
        using var temp = new TempDirectory();
        var executor = Substitute.For<ITaskExecutor>();
        var provider = new StubChatProvider { CompletionResult = new ChatCompletion(new ChatMessage(ChatRole.Assistant, "not json")) };
        // two completions return bad json (initial + repair)
        var calls = 0;
        provider = null!;
        var provider2 = Substitute.For<IChatProvider>();
        provider2.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => { calls++; return Task.FromResult(new ChatCompletion(new ChatMessage(ChatRole.Assistant, "not json"))); });
        var services = CreateServices(temp.Path, executor: executor, chatProvider: provider2);
        var console = new FakeCliConsole(interactive: true);
        var command = new TaskCommand(console, services);

        var code = await command.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["do x"] });

        Assert.Equal(CliExitCodes.Error, code);
        Assert.Equal(2, calls); // one repair attempt only
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [Fact]
    public async Task TaskRunProviderDisconnectFailsSafely()
    {
        using var temp = new TempDirectory();
        var executor = Substitute.For<ITaskExecutor>();
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatCompletion>>(_ => throw new HttpRequestException("connection refused"));
        var services = CreateServices(temp.Path, executor: executor, chatProvider: provider);
        var console = new FakeCliConsole(interactive: true);
        var command = new TaskCommand(console, services);

        var code = await command.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["do x"] });

        Assert.Equal(CliExitCodes.Error, code);
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        Assert.Contains("failed", console.Out + console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskRunCancellationDuringExecutionReportsCancelled()
    {
        using var temp = new TempDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<TaskOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = Substitute.For<ITaskExecutor>();
        executor.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                entered.TrySetResult();
                var ct = call.Arg<CancellationToken>();
                try { await never.Task.WaitAsync(ct); } catch (OperationCanceledException) { throw; }
                return await never.Task;
            });
        var services = CreateServices(temp.Path, executor: executor,
            providerResult: "{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}");
        var console = new FakeCliConsole(["approve PLAN"], interactive: true);
        var command = new TaskCommand(console, services);
        using var cts = new CancellationTokenSource();

        var running = command.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["open notepad"] }, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        var code = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(CliExitCodes.Cancelled, code);
    }

    [Fact]
    public async Task StopResetStatusHistorySupportJsonAndSharedState()
    {
        using var temp = new TempDirectory();
        var services = CreateServices(temp.Path);
        var control = services.TaskControl!;
        var history = services.TaskHistory!;
        var id = Guid.NewGuid();
        history.Record(new TaskHistoryEntry(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", 1, 1, "ollama", "qwen3.5:4b", true, true, "Completed"));

        var stopConsole = new FakeCliConsole(interactive: false);
        var stopCode = await new TaskCommand(stopConsole, services).ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["stop"], Json = true });
        Assert.Equal(CliExitCodes.Success, stopCode);
        Assert.Contains("\"stopped\": true", stopConsole.Out, StringComparison.OrdinalIgnoreCase);
        Assert.True(control.GetStopState().Stopped);

        var statusConsole = new FakeCliConsole(interactive: false);
        await new TaskCommand(statusConsole, services).ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["status", id.ToString()], Json = true });
        Assert.Contains("\"metadata\"", statusConsole.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"target\"", statusConsole.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"text\"", statusConsole.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", statusConsole.Out, StringComparison.OrdinalIgnoreCase);

        var listConsole = new FakeCliConsole(interactive: false);
        await new TaskCommand(listConsole, services).ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Tasks, Json = true });
        Assert.Contains(id.ToString(), listConsole.Out, StringComparison.OrdinalIgnoreCase);

        var historyConsole = new FakeCliConsole(interactive: false);
        await new TaskCommand(historyConsole, services).ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["history"], Json = true });
        Assert.Contains(id.ToString(), historyConsole.Out, StringComparison.OrdinalIgnoreCase);

        var resetConsole = new FakeCliConsole(interactive: false);
        await new TaskCommand(resetConsole, services).ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["reset-stop"], Json = true });
        Assert.Contains("never revived", resetConsole.Out, StringComparison.OrdinalIgnoreCase);
        Assert.False(control.GetStopState().Stopped);
    }

    [Fact]
    public async Task CliTaskApprovalInvalidWorkspaceAndTimeoutAreRejected()
    {
        var plan = TaskPlanParser.Parse("{\"steps\":[{\"tool\":\"create_note\",\"target\":\"n.txt\",\"text\":\"x\"}]}");
        // invalid workspace path then cancel
        var console = new FakeCliConsole(["2", "C:\\Windows\\System32", "3"], interactive: true);
        var approval = new CliTaskApproval(console, TimeSpan.FromSeconds(2));
        var result = await approval.ApproveAsync(plan, CancellationToken.None);
        Assert.Null(result);
        Assert.Contains("refusing", console.Error, StringComparison.OrdinalIgnoreCase);

        // timeout: no decision provided
        var slow = new FakeCliConsole(["1"], interactive: true); // selects temp workspace candidate, then EOF/no decision
        var approvalTimeout = new CliTaskApproval(slow, TimeSpan.FromMilliseconds(250));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var timed = await approvalTimeout.ApproveAsync(plan, cts.Token);
        Assert.Null(timed);
    }

    private static StubCliServices CreateServices(
        string tempPath,
        ITaskExecutor? executor = null,
        IChatProvider? chatProvider = null,
        string? providerResult = null)
    {
        var paths = new CapriPaths(tempPath);
        var settings = CapriDefaultSettings.Create();
        settings = settings with
        {
            Provider = settings.Provider with
            {
                ProviderId = ProviderCatalog.OllamaId,
                BaseUrl = ProviderCatalog.Ollama.DefaultBaseUrl,
                ChatModel = "qwen3.5:4b",
                AllowCloudInference = false,
                FreeModelsOnly = true,
            }
        };

        var provider = chatProvider ?? new StubChatProvider
        {
            ProviderId = "ollama",
            CompletionResult = new ChatCompletion(new ChatMessage(ChatRole.Assistant, providerResult ?? "{\"steps\":[]}"))
        };
        var router = new StubModelRouter("ollama", "qwen3.5:4b", supportsStreaming: false);
        var control = new FileTaskControl(paths);
        var history = new FileTaskHistoryStore(paths);
        var journal = new TaskMetadataJournal(paths, history);

        var services = new StubCliServices(paths, new FakeAppSettingsStore(settings), new FakeCredentialStore(), provider, router)
        {
            TaskControl = control,
            TaskHistory = history,
            TaskExecutor = executor ?? Substitute.For<ITaskExecutor>(),
            TaskJournal = journal,
        };
        // default executor success for convenience in specific tests
        if (executor is null)
        {
            services.TaskExecutor!.ExecuteAsync(Arg.Any<TaskStep>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new TaskOutcome(true, false, "dispatched"));
        }
        return services;
    }
}
