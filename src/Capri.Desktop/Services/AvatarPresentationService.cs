using Capri.Core.Abstractions;
using Capri.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Desktop.Services;

/// <summary>
/// Bridges <see cref="IAvatarStateMachine"/> state changes to the UI thread and applies
/// desktop-only dwell behavior (Success/Error return to Idle). Respects reduce-motion from settings.
/// </summary>
public sealed partial class AvatarPresentationService : ObservableObject, IDisposable
{
    private static readonly TimeSpan DwellDuration = TimeSpan.FromMilliseconds(1500);

    private readonly IAvatarStateMachine _stateMachine;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<AvatarPresentationService> _logger;
    private readonly object _timerSync = new();
    private CancellationTokenSource? _dwellCts;
    private bool _disposed;

    /// <summary>Current visual state for binding.</summary>
    [ObservableProperty]
    private AvatarState _displayState = AvatarState.Idle;

    /// <summary>True when motion should be reduced according to saved UI settings.</summary>
    [ObservableProperty]
    private bool _reduceMotion;

    public AvatarPresentationService(
        IAvatarStateMachine stateMachine,
        IAppSettingsStore settingsStore,
        IUiDispatcher dispatcher,
        ILogger<AvatarPresentationService>? logger = null)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? NullLogger<AvatarPresentationService>.Instance;

        _displayState = _stateMachine.Current;
        ApplySettings(_settingsStore.Current);

        _stateMachine.StateChanged += OnStateChanged;
        _settingsStore.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs e)
    {
        _dispatcher.Invoke(() => ApplySettings(e.Current));
    }

    private void ApplySettings(AppSettings settings)
    {
        ReduceMotion = settings.Ui.ReduceMotion;
    }

    private void OnStateChanged(object? sender, AvatarStateChangedEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            CancelDwell();
            DisplayState = e.Current;

            if (e.Current is AvatarState.Success or AvatarState.Error or AvatarState.Happy or AvatarState.Annoyed or AvatarState.Surprised)
            {
                StartDwellToIdle();
            }
        });
    }

    private void StartDwellToIdle()
    {
        CancellationTokenSource cts;
        lock (_timerSync)
        {
            _dwellCts?.Cancel();
            _dwellCts?.Dispose();
            _dwellCts = new CancellationTokenSource();
            cts = _dwellCts;
        }

        _ = DwellAndReturnToIdleAsync(cts.Token);
    }

    private async Task DwellAndReturnToIdleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DwellDuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _dispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                _stateMachine.TryTransition(AvatarState.Idle, AvatarTransitionReason.Timeout);
            }
            catch (Exception ex)
            {
                LogAvatarDwellTransitionFailed(_logger, ex);
            }
        });
    }

    private void CancelDwell()
    {
        lock (_timerSync)
        {
            _dwellCts?.Cancel();
            _dwellCts?.Dispose();
            _dwellCts = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelDwell();
        _stateMachine.StateChanged -= OnStateChanged;
        _settingsStore.Changed -= OnSettingsChanged;
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Avatar dwell transition to Idle failed.")]
    private static partial void LogAvatarDwellTransitionFailed(ILogger logger, Exception exception);
}
