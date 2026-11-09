using System.IO.Pipes;
using Capri.Core.Autonomy;
using Capri.Security.Autonomy;
using NSubstitute;

namespace Capri.Tests.Autonomy;

public sealed class TaskStatusPipeTests
{
    [Fact]
    public async Task CurrentUserPipeReturnsOnlyBoundedTaskControlMetadata()
    {
        var name = TestPipeName();
        var taskId = Guid.NewGuid();
        var control = Substitute.For<ITaskControl>();
        control.GetActive().Returns(new TaskOwnership(taskId, Environment.ProcessId, DateTimeOffset.UtcNow, Environment.MachineName));
        control.GetStopState().Returns(new TaskStopState(false, 3, DateTimeOffset.UtcNow));

        using var cancellation = new CancellationTokenSource();
        var running = Task.Run(() => TaskStatusPipe.ServeAsync(control, cancellation.Token, name));
        try
        {
            var status = await QueryWhenReadyAsync(name);
            Assert.NotNull(status);
            Assert.Equal(1, status.Version);
            Assert.Equal(taskId, status.ActiveTaskId);
            Assert.Equal(Environment.ProcessId, status.ActiveProcessId);
            Assert.False(status.Stopped);
            Assert.Equal(3, status.StopGeneration);
            Assert.True(status.CheckedUtc > DateTimeOffset.UtcNow.AddMinutes(-1));

            // No requester can take task ownership, stop tasks, or approve anything.
            control.DidNotReceiveWithAnyArgs().RequestStop(default);
            control.DidNotReceive().ResetStop();
            control.DidNotReceiveWithAnyArgs().TryAcquire(default!);
            control.DidNotReceiveWithAnyArgs().Release(default);
        }
        finally
        {
            cancellation.Cancel();
            await AssertCancelledOrCompletedAsync(running);
        }
    }

    [Fact]
    public async Task UnknownOpcodeCannotReadTaskStatusOrChangeControl()
    {
        var name = TestPipeName();
        var control = Substitute.For<ITaskControl>();
        using var cancellation = new CancellationTokenSource();
        var running = Task.Run(() => TaskStatusPipe.ServeAsync(control, cancellation.Token, name));
        try
        {
            using var client = new NamedPipeClientStream(
                ".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(2000, CancellationToken.None);
            await client.WriteAsync(new byte[] { 0xff });
            await client.FlushAsync();

            using var readDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var read = await client.ReadAsync(new byte[1], readDeadline.Token);
            Assert.Equal(0, read);

            control.DidNotReceive().GetStopState();
            control.DidNotReceive().GetActive();
            control.DidNotReceiveWithAnyArgs().RequestStop(default);
            control.DidNotReceive().ResetStop();
        }
        finally
        {
            cancellation.Cancel();
            await AssertCancelledOrCompletedAsync(running);
        }
    }

    [Fact]
    public async Task MissingDesktopListenerDoesNotFabricateStatus()
    {
        var result = await TaskStatusPipe.QueryAsync(
            CancellationToken.None, TestPipeName());
        Assert.Null(result);
    }

    [Fact]
    public void PipeNameIdentifiesCurrentWindowsUserAndVersion()
    {
        Assert.StartsWith("capri-task-status-v1-S-1-", TaskStatusPipe.CurrentUserPipeName());
    }

    private static string TestPipeName() =>
        TaskStatusPipe.CurrentUserPipeName() + "-test-" + Guid.NewGuid().ToString("N");

    private static async Task<TaskDesktopStatus?> QueryWhenReadyAsync(string name)
    {
        for (var i = 0; i < 20; i++)
        {
            var status = await TaskStatusPipe.QueryAsync(CancellationToken.None, name);
            if (status is not null) return status;
            await Task.Delay(50);
        }
        return null;
    }

    private static async Task AssertCancelledOrCompletedAsync(Task running)
    {
        try
        {
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // Expected when the service is stopped during a test.
        }
    }
}
