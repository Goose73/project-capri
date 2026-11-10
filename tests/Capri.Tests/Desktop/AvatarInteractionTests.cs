using Capri.Core.Avatar;
using Capri.Desktop.Services;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Desktop;

/// <summary>Headless checks; no windows, cursor access or screen capture.</summary>
public sealed class AvatarInteractionTests
{
    [Fact]
    public void ExplicitScreenQuestionTurnsTowardScreenThenReturnsToUser()
    {
        using var presentation = NewPresentation();
        var cues = new List<AvatarCueKind>();
        presentation.CueRequested += (_, e) => cues.Add(e.Cue);

        presentation.ObserveUserPrompt("Hey Capri, look at my screen and explain this.");
        presentation.FinishUserPrompt();
        presentation.FinishUserPrompt();

        Assert.Equal([AvatarCueKind.ScreenRequested, AvatarCueKind.ReturnToUser], cues);
    }

    [Fact]
    public void NonScreenPromptsNeverPretendToInspectDesktop()
    {
        using var presentation = NewPresentation();
        var cues = new List<AvatarCueKind>();
        presentation.CueRequested += (_, e) => cues.Add(e.Cue);

        presentation.ObserveUserPrompt("Tell me how LCD screens work.");
        presentation.ObserveUserPrompt("Don't look at my screen.");
        presentation.FinishUserPrompt();

        Assert.Empty(cues);
    }

    [Fact]
    public void DisposedPresentationNeverEmitsNewReactions()
    {
        var presentation = NewPresentation();
        var cues = new List<AvatarCueKind>();
        presentation.CueRequested += (_, e) => cues.Add(e.Cue);

        presentation.Dispose();
        presentation.ObserveUserPrompt("Look at my screen");
        presentation.FinishUserPrompt();

        Assert.Empty(cues);
    }

    private static AvatarPresentationService NewPresentation() =>
        new(new AvatarStateMachine(), new FakeAppSettingsStore(), new ImmediateDispatcher());

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Invoke(Action action) => action();
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(func());
        }
    }
}
