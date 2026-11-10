using Capri.Core.Companion;
using Capri.Core.Models;
using Capri.Desktop.Services;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Desktop;

public sealed class AmbientCompanionServiceTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, .. Enumerable.Repeat((byte)1, 256), 0xFF, 0xD9];

    [Fact]
    public async Task SettingsAloneNeverStartCameraAndExplicitConsentIsRequired()
    {
        var store = CreateSettings(enabled: true);
        var camera = new FakeCamera();
        var vision = new FakeVision();
        var speech = new FakeSpeech();
        using var companion = new AmbientCompanionService(
            store, camera, vision, speech, new ImmediateDispatcher());

        Assert.False(companion.IsObserving);
        Assert.Equal(0, camera.Captures);
        Assert.False(await companion.StartSessionAsync(confirmed: false));
        Assert.Equal(0, camera.Captures);
        Assert.Equal(0, vision.ModelChecks);

        Assert.True(await companion.StartSessionAsync(confirmed: true));
        Assert.True(companion.IsObserving);
        await WaitForAsync(() => camera.Captures > 0);
        companion.Stop();
        Assert.False(companion.IsObserving);
        Assert.False(companion.IsPaused);
    }

    [Fact]
    public async Task SpokenCommentsNeedSeparateOptInAndAreSuppressedWhileBusy()
    {
        var store = CreateSettings(enabled: true, comments: true, speech: false);
        var camera = new FakeCamera();
        var vision = new FakeVision();
        var speech = new FakeSpeech();
        using var companion = new AmbientCompanionService(
            store, camera, vision, speech, new ImmediateDispatcher());
        var remarks = new List<string>();
        companion.RemarkReady += (_, e) => remarks.Add(e.Text);

        companion.SetConversationBusy(true);
        Assert.True(await companion.StartSessionAsync(confirmed: true));
        await WaitForAsync(() => camera.Captures > 0);
        await Task.Delay(200);
        Assert.Empty(remarks);
        companion.Stop();
        Assert.Equal(0, speech.Spoken);
    }

    [Fact]
    public async Task PausingRevokesCaptureAndCanResumeOnlyWithinExplicitlyConsentedSession()
    {
        var store = CreateSettings(enabled: true);
        var camera = new FakeCamera();
        using var companion = new AmbientCompanionService(
            store, camera, new FakeVision(), new FakeSpeech(), new ImmediateDispatcher());

        Assert.True(await companion.StartSessionAsync(confirmed: true));
        await WaitForAsync(() => camera.Captures > 0);
        companion.Pause();
        Assert.False(companion.IsObserving);
        Assert.True(companion.IsPaused);
        Assert.True(await companion.ResumeAsync());
        Assert.True(companion.IsObserving);
        companion.Stop();
        Assert.False(await companion.ResumeAsync());
    }

    [Fact]
    public async Task DisablingPreferenceImmediatelyRevokesSession()
    {
        var store = CreateSettings(enabled: true);
        var camera = new FakeCamera();
        using var companion = new AmbientCompanionService(
            store, camera, new FakeVision(), new FakeSpeech(), new ImmediateDispatcher());
        Assert.True(await companion.StartSessionAsync(confirmed: true));
        await store.UpdateAsync(store.Current with
        {
            AmbientVision = store.Current.AmbientVision with { Enabled = false }
        });
        Assert.False(companion.IsObserving);
        Assert.False(await companion.StartSessionAsync(confirmed: true));
    }

    [Fact]
    public async Task LocalVisionMustBeInstalledBeforeCameraCanStart()
    {
        var camera = new FakeCamera();
        using var companion = new AmbientCompanionService(
            CreateSettings(enabled: true), camera, new FakeVision(ready: false),
            new FakeSpeech(), new ImmediateDispatcher());
        Assert.False(await companion.StartSessionAsync(confirmed: true));
        Assert.Equal(0, camera.Captures);
        Assert.False(companion.IsObserving);
    }

    private static FakeAppSettingsStore CreateSettings(bool enabled, bool comments = false, bool speech = false)
        => new(new AppSettings
        {
            AmbientVision = new AmbientVisionSettings
            {
                Enabled = enabled,
                AllowSpontaneousComments = comments,
                SpeakRemarks = speech,
                LocalOnly = true,
                SampleIntervalSeconds = 20,
                MinimumCommentIntervalSeconds = 300
            }
        });

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class FakeCamera : IAmbientCameraCapture
    {
        private int _captures;
        public int Captures => Volatile.Read(ref _captures);
        public Task<byte[]> CaptureJpegAsync(int index, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _captures);
            return Task.FromResult(Jpeg.ToArray());
        }
    }

    private sealed class FakeVision(bool ready = true) : IAmbientVisionClient
    {
        private int _checks;
        public int ModelChecks => Volatile.Read(ref _checks);
        public Task<bool> IsModelReadyAsync(string model, CancellationToken ct)
        {
            Interlocked.Increment(ref _checks);
            return Task.FromResult(ready);
        }
        public Task<AmbientVisionObservation> ObserveAsync(string model, byte[] jpeg, CancellationToken ct)
            => Task.FromResult(new AmbientVisionObservation(true, "Nice workspace!"));
    }

    private sealed class FakeSpeech : IAmbientCommentSpeech
    {
        public int Spoken { get; private set; }
        public Task SayAsync(string text, CancellationToken ct)
        {
            Spoken++;
            return Task.CompletedTask;
        }
    }

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
        public Task<T> InvokeAsync<T>(Func<T> fn, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(fn());
        }
    }
}
