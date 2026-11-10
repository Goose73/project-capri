using Capri.Core.Abstractions;
using Capri.Core.Companion;
using Capri.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

namespace Capri.Desktop.Services;

/// <summary>A short, non-sensitive, transient optional remark produced locally.</summary>
public sealed class AmbientRemarkEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}

/// <summary>
/// Session-scoped opt-in webcam awareness. This singleton NEVER opens a camera
/// on construction, startup, settings load or application restart. A user must
/// press Start, acknowledge the dialog and have enabled the preference.
/// </summary>
public sealed partial class AmbientCompanionService : ObservableObject, IDisposable
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IAmbientCameraCapture _camera;
    private readonly IAmbientVisionClient _vision;
    private readonly IAmbientCommentSpeech _speech;
    private readonly IUiDispatcher _dispatcher;
    private readonly object _sync = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private int _generation;
    private bool _sessionConsented;
    private bool _busy;
    private bool _disposed;
    private string? _lastComment;

    [ObservableProperty] private bool _isObserving;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _statusText = "Webcam awareness off";

    /// <summary>No images, transcripts, profiles or frame data in this event.</summary>
    public event EventHandler<AmbientRemarkEventArgs>? RemarkReady;

    public AmbientCompanionService(
        IAppSettingsStore settingsStore,
        IAmbientCameraCapture camera,
        IAmbientVisionClient vision,
        IAmbientCommentSpeech speech,
        IUiDispatcher dispatcher)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _vision = vision ?? throw new ArgumentNullException(nameof(vision));
        _speech = speech ?? throw new ArgumentNullException(nameof(speech));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _settingsStore.Changed += SettingsStore_Changed;
        if (OperatingSystem.IsWindows())
            SystemEvents.SessionSwitch += SessionSwitch;
    }

    /// <summary>
    /// Only the UI's explicit Start + webcam information dialog passes confirmed=true.
    /// A saved setting does not grant or restore consent.
    /// </summary>
    public async Task<bool> StartSessionAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return false;
        var settings = _settingsStore.Current.AmbientVision;
        if (!confirmed || !settings.Enabled || !settings.LocalOnly ||
            settings.CameraIndex is < 0 or > 4 ||
            !LocalOllamaAmbientVision.IsValidModel(settings.VisionModel))
        {
            SetStatus("Enable Ambient Companion in settings and confirm camera access to begin.");
            return false;
        }

        int generation;
        lock (_sync)
        {
            if (_runCancellation is not null) return IsObserving;
            generation = ++_generation;
        }

        SetStatus("Checking the installed local Ollama vision model...");
        bool ready;
        try
        {
            ready = await _vision.IsModelReadyAsync(settings.VisionModel, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            ready = false;
        }

        if (!ready)
        {
            SetStatus("Local vision model unavailable. Start Ollama and install the selected vision model.");
            return false;
        }

        lock (_sync)
        {
            if (_disposed || _generation != generation || _runCancellation is not null)
                return false;
            _runCancellation = new CancellationTokenSource();
            _sessionConsented = true;
            var runToken = _runCancellation.Token;
            _runTask = Task.Run(() => RunLoopAsync(generation, settings, runToken));
        }

        OnUi(() =>
        {
            IsObserving = true;
            IsPaused = false;
            StatusText = "CAMERA ON — local snapshots only. Stop or pause anytime.";
        });
        return true;
    }

    /// <summary>Immediately revoke consent and cancel all outstanding model/speech work.</summary>
    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            _generation++;
            _sessionConsented = false;
            cancellation = _runCancellation;
            _runCancellation = null;
        }

        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        OnUi(() =>
        {
            IsObserving = false;
            IsPaused = false;
            StatusText = "Webcam awareness off";
        });
    }

    /// <summary>Pause revokes active capture but keeps consent until this app session ends.</summary>
    public void Pause()
    {
        if (!IsObserving) return;
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            _generation++;
            cancellation = _runCancellation;
            _runCancellation = null;
        }
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        OnUi(() =>
        {
            IsObserving = false;
            IsPaused = true;
            StatusText = "Webcam paused — no new snapshots or comments.";
        });
    }

    public Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
    {
        var consent = _sessionConsented && IsPaused && !_disposed;
        return StartSessionAsync(consent, cancellationToken);
    }

    /// <summary>Suppressed commentary while Capri is working on a user request.</summary>
    public void SetConversationBusy(bool busy) => _busy = busy;

    private async Task RunLoopAsync(int generation, AmbientVisionSettings settings, CancellationToken token)
    {
        DateTimeOffset? lastCapture = null;
        DateTimeOffset? lastComment = null;
        string? lastRemark = null;
        try
        {
            while (!token.IsCancellationRequested && IsCurrent(generation))
            {
                // This opens the camera only AFTER explicit consent, under a
                // persistent ON indicator. The OS privacy broker is verified by
                // successfully opening/reading the device, not assumed.
                var jpeg = await _camera.CaptureJpegAsync(settings.CameraIndex, token)
                    .ConfigureAwait(false);
                try
                {
                    if (!IsCurrent(generation)) break;
                    var now = DateTimeOffset.UtcNow;
                    var runtime = new AmbientVisionRuntime(
                        SessionConsentGiven: true,
                        WindowsCameraAccessGranted: true, // successful native device read
                        CameraAvailable: true, // successful native device read
                        VisibleIndicatorActive: IsObserving,
                        LocalVisionModelAvailable: true, // local tags checked before start
                        Paused: false,
                        SessionLocked: false, // SessionSwitch stops the session on lock
                        CaptureInProgress: false,
                        LastCapturedUtc: lastCapture,
                        LastCommentUtc: lastComment,
                        NewMeaningfulObservation: false,
                        UserIsBusy: _busy);
                    var decision = AmbientVisionPolicy.Evaluate(settings, runtime, now);
                    if (!decision.CaptureFrame)
                    {
                        await DelayUntilNextAsync(settings, token).ConfigureAwait(false);
                        continue;
                    }

                    var observation = await _vision.ObserveAsync(settings.VisionModel, jpeg, token)
                        .ConfigureAwait(false);
                    lastCapture = DateTimeOffset.UtcNow;
                    if (!IsCurrent(generation)) break;

                    var commentDecision = AmbientVisionPolicy.Evaluate(settings,
                        runtime with
                        {
                            LastCapturedUtc = lastCapture,
                            NewMeaningfulObservation = observation.Meaningful,
                            LastCommentUtc = lastComment,
                            UserIsBusy = _busy
                        }, lastCapture.Value);
                    var remark = observation.Remark?.Trim();
                    if (commentDecision.MakeSpontaneousRemark &&
                        !string.IsNullOrWhiteSpace(remark) &&
                        !string.Equals(lastRemark, remark, StringComparison.OrdinalIgnoreCase))
                    {
                        lastComment = lastCapture;
                        lastRemark = remark;
                        if (IsCurrent(generation))
                        {
                            OnUi(() =>
                            {
                                if (IsCurrent(generation))
                                    RemarkReady?.Invoke(this, new AmbientRemarkEventArgs(remark));
                            });
                        }

                        if (settings.SpeakRemarks && IsCurrent(generation))
                            await _speech.SayAsync(remark, token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    Array.Clear(jpeg);
                }

                if (IsCurrent(generation))
                    await DelayUntilNextAsync(settings, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when user pauses/stops or Windows locks.
        }
        catch (Exception)
        {
            // Do not log webcam frames, descriptions, image data or model responses.
            // A capture/model failure stops rather than retrying camera access silently.
            if (IsCurrent(generation))
            {
                OnUi(() => StatusText =
                    "Camera or local vision request failed. Webcam awareness stopped; retry explicitly.");
                StopAfterFailure(generation);
            }
        }
        finally
        {
            lock (_sync)
            {
                // Only this run may release its own CTS; a new session never
                // observes or reuses cancelled previous-session resources.
                if (_generation == generation && _runCancellation is not null)
                {
                    _runCancellation.Dispose();
                    _runCancellation = null;
                }
            }
        }
    }

    private static Task DelayUntilNextAsync(AmbientVisionSettings settings, CancellationToken token)
        => Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.SampleIntervalSeconds, 10, 3600)), token);

    private bool IsCurrent(int generation)
    {
        lock (_sync)
            return !_disposed && _sessionConsented && _generation == generation &&
                   _runCancellation is { IsCancellationRequested: false };
    }

    private void StopAfterFailure(int generation)
    {
        lock (_sync)
        {
            if (_generation != generation) return;
            _generation++;
            _sessionConsented = false;
            _runCancellation?.Cancel();
            _runCancellation = null;
        }
        OnUi(() =>
        {
            IsObserving = false;
            IsPaused = false;
            StatusText = "Camera session stopped after a capture/model error.";
        });
    }

    private void SetStatus(string text) => OnUi(() => StatusText = text);

    private void OnUi(Action action)
    {
        if (_disposed) return;
        try { _dispatcher.Invoke(action); }
        catch (InvalidOperationException) { /* Dispatcher shutting down */ }
    }

    private void SettingsStore_Changed(object? sender, Capri.Core.Models.AppSettingsChangedEventArgs e)
    {
        if (!e.Current.AmbientVision.Enabled || !e.Current.AmbientVision.LocalOnly)
            Stop();
    }

    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _settingsStore.Changed -= SettingsStore_Changed;
        if (OperatingSystem.IsWindows())
            SystemEvents.SessionSwitch -= SessionSwitch;
        GC.SuppressFinalize(this);
    }
}
