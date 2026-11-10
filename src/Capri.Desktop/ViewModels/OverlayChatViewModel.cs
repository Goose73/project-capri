using System.Collections.ObjectModel;
using System.Text;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;
using Capri.Core.Models;
using Capri.Desktop.Services;
using Capri.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Desktop.ViewModels;

public sealed partial class OverlayChatViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(75);

    private readonly IAssistantOrchestrator _orchestrator;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<OverlayChatViewModel> _logger;
    private readonly TaskCoordinator? _tasks;
    private readonly DesktopTaskAuthorityService? _authority;

    /// <summary>User-controlled, session-scoped webcam companion, never started automatically.</summary>
    public AmbientCompanionService? Ambient { get; }
    private readonly StringBuilder _pendingDelta = new();
    private readonly object _deltaSync = new();
    private readonly System.Threading.Timer _flushTimer;
    private CancellationTokenSource? _activeCts;
    private ChatMessageViewModel? _streamingAssistantMessage;
    private bool _disposed;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _modelStatus = string.Empty;

    [ObservableProperty]
    private string _authorityStatus = "Task authority: starting...";

    [ObservableProperty]
    private string _activeTaskStatus = "Active task: none";

    public OverlayChatViewModel(
        IAssistantOrchestrator orchestrator,
        IAppSettingsStore settingsStore,
        ISecureCredentialStore credentialStore,
        IUiDispatcher dispatcher,
        ILogger<OverlayChatViewModel>? logger = null,
        TaskCoordinator? tasks = null,
        DesktopTaskAuthorityService? authority = null,
        AmbientCompanionService? ambient = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? NullLogger<OverlayChatViewModel>.Instance;
        _tasks = tasks;
        _authority = authority;
        Ambient = ambient;
        if (Ambient is not null) Ambient.RemarkReady += OnAmbientRemark;

        _flushTimer = new System.Threading.Timer(_ => FlushPendingDelta(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _settingsStore.Changed += OnSettingsChanged;
        if (_authority is not null)
        {
            _authority.StateChanged += OnAuthorityStateChanged;
            OnAuthorityStateChanged(null, EventArgs.Empty);
        }
        UpdateModelStatus(_settingsStore.Current);
    }

    private void OnAmbientRemark(object? sender, AmbientRemarkEventArgs e)
    {
        if (_disposed || IsBusy) return;
        _dispatcher.Invoke(() =>
        {
            if (!_disposed && !IsBusy)
                Messages.Add(new ChatMessageViewModel("Capri", e.Text));
        });
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs e)
    {
        _dispatcher.Invoke(() => UpdateModelStatus(e.Current));
    }

    private void UpdateModelStatus(AppSettings settings)
    {
        var provider = settings.Provider.ProviderId;
        var model = settings.Provider.ChatModel;
        ModelStatus = string.IsNullOrWhiteSpace(model)
            ? "No model configured"
            : $"{provider}: {model}";
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsBusy)
        {
            return;
        }

        // Do not block local providers that require no API key. Missing/invalid credentials
        // are surfaced as friendly provider errors (including turn correlation) by the pipeline.
        _dispatcher.Invoke(() =>
        {
            InputText = string.Empty;
            Messages.Add(new ChatMessageViewModel("You", text));
            IsBusy = true;
            Ambient?.SetConversationBusy(true);
            StatusText = "Thinking...";
        });

        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = new CancellationTokenSource();
        var ct = _activeCts.Token;

        try
        {
            await RunTurnAsync(text, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _dispatcher.Invoke(() =>
            {
                FlushPendingDelta();
                StatusText = "Cancelled";
                if (_streamingAssistantMessage is not null && string.IsNullOrWhiteSpace(_streamingAssistantMessage.Text))
                {
                    _streamingAssistantMessage.Text = "(Cancelled)";
                }
            });
        }
        catch (Exception ex)
        {
            LogUnexpectedChatFailure(_logger, ex);
            _dispatcher.Invoke(() =>
            {
                FlushPendingDelta();
                Messages.Add(new ChatMessageViewModel("System", "Something went wrong while processing your request.", isError: true));
                StatusText = "Error";
            });
        }
        finally
        {
            _dispatcher.Invoke(() =>
            {
                FlushPendingDelta();
                StopFlushTimer();
                _streamingAssistantMessage = null;
                IsBusy = false;
                Ambient?.SetConversationBusy(false);
                if (string.Equals(StatusText, "Thinking...", StringComparison.Ordinal) ||
                    string.Equals(StatusText, "Responding...", StringComparison.Ordinal))
                {
                    StatusText = string.Empty;
                }
            });
        }
    }

    private async Task RunTurnAsync(string text, CancellationToken ct)
    {
        await foreach (var update in _orchestrator.StreamAsync(text, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            HandleUpdate(update);
        }
    }

    private void HandleUpdate(AssistantUpdate update)
    {
        switch (update.Kind)
        {
            case AssistantUpdateKind.Started:
                _dispatcher.Invoke(() => StatusText = "Thinking...");
                break;

            case AssistantUpdateKind.IntentDetected:
                // Optional: could show intent; keep UI clean.
                break;

            case AssistantUpdateKind.TextDelta:
                if (!string.IsNullOrEmpty(update.Text))
                {
                    EnsureStreamingMessage();
                    EnqueueDelta(update.Text);
                }
                break;

            case AssistantUpdateKind.ToolExecuted:
                FlushPendingDelta();
                _dispatcher.Invoke(() =>
                {
                    var msg = update.ToolResult?.Message;
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        Messages.Add(new ChatMessageViewModel("Capri", msg, isError: update.ToolResult?.Succeeded == false));
                    }
                });
                break;

            case AssistantUpdateKind.Completed:
                FlushPendingDelta();
                _dispatcher.Invoke(() =>
                {
                    // Orchestrator may return final text for automation; streaming text already appended.
                    if (_streamingAssistantMessage is null && !string.IsNullOrWhiteSpace(update.Text))
                    {
                        Messages.Add(new ChatMessageViewModel("Capri", update.Text));
                    }
                    StatusText = string.Empty;
                });
                break;

            case AssistantUpdateKind.Failed:
                FlushPendingDelta();
                _dispatcher.Invoke(() =>
                {
                    var friendly = FormatFriendlyError(update.Error, update.Text);
                    if (_streamingAssistantMessage is not null)
                    {
                        _streamingAssistantMessage.IsError = true;
                        if (string.IsNullOrWhiteSpace(_streamingAssistantMessage.Text))
                        {
                            _streamingAssistantMessage.Text = friendly;
                        }
                        else
                        {
                            _streamingAssistantMessage.Append("\n\n" + friendly);
                        }
                    }
                    else
                    {
                        Messages.Add(new ChatMessageViewModel("Capri", friendly, isError: true));
                    }
                    StatusText = "Error";
                });
                break;
        }
    }

    private static string FormatFriendlyError(ProviderError? error, string? fallbackText)
    {
        if (error is not null)
        {
            // ProviderError.Message from orchestrator includes turn id; safe to display (no secrets/stack traces).
            var kind = error.Kind == ProviderErrorKind.Unknown ? string.Empty : $" ({error.Kind})";
            return string.IsNullOrWhiteSpace(error.Code)
                ? $"{error.Message}{kind}"
                : $"{error.Message}{kind} [code: {error.Code}]";
        }

        return string.IsNullOrWhiteSpace(fallbackText)
            ? "The request could not be completed."
            : fallbackText;
    }

    private void EnsureStreamingMessage()
    {
        if (_streamingAssistantMessage is not null)
        {
            return;
        }

        _dispatcher.Invoke(() =>
        {
            if (_streamingAssistantMessage is not null)
            {
                return;
            }

            _streamingAssistantMessage = new ChatMessageViewModel("Capri", string.Empty);
            Messages.Add(_streamingAssistantMessage);
            StatusText = "Responding...";
            StartFlushTimer();
        });
    }

    private void StartFlushTimer()
    {
        _flushTimer.Change(FlushInterval, FlushInterval);
    }

    private void StopFlushTimer()
    {
        _flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void EnqueueDelta(string delta)
    {
        lock (_deltaSync)
        {
            _pendingDelta.Append(delta);
        }
    }

    private void FlushPendingDelta()
    {
        string text;
        lock (_deltaSync)
        {
            if (_pendingDelta.Length == 0)
            {
                return;
            }

            text = _pendingDelta.ToString();
            _pendingDelta.Clear();
        }

        _dispatcher.Invoke(() =>
        {
            _streamingAssistantMessage?.Append(text);
        });
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _activeCts?.Cancel();
    }

    private void OnAuthorityStateChanged(object? sender, EventArgs e)
    {
        if (_authority is null) return;
        var status = _authority.ConnectionStatus;
        var state = _authority.LastState;
        var active = state?.ActiveTask is null
            ? "Active task: none"
            : $"Active task: {state.ActiveTask.TaskId:N} | {state.ActiveTask.Phase} | step {state.ActiveTask.StepIndex}{(state.ActiveTask.TotalSteps.HasValue ? "/" + state.ActiveTask.TotalSteps : "")}{(state.PendingApproval is not null ? " | APPROVAL REQUIRED" : "")}";
        if (state?.Stopped == true) active += $" | STOP LATCHED (gen {state.StopGeneration})";
        _dispatcher.Invoke(() =>
        {
            AuthorityStatus = "Task authority: " + status;
            ActiveTaskStatus = active;
        });
    }

    [RelayCommand]
    private async Task EmergencyStopAsync()
    {
        _activeCts?.Cancel();
        if (_authority is not null)
        {
            var resp = await _authority.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.Stop)).ConfigureAwait(false);
            StatusText = resp.Ok ? "Emergency stop requested via task authority." : ("Stop failed: " + (resp.Error ?? "unknown"));
            return;
        }
        _tasks?.Stop();
        StatusText = "Task STOP latched. Reset explicitly to allow tasks.";
    }

    [RelayCommand]
    private async Task ResetTaskStopAsync()
    {
        if (_authority is not null)
        {
            var resp = await _authority.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.ResetStop)).ConfigureAwait(false);
            StatusText = resp.Ok ? "Task stop reset via authority." : ("Reset failed: " + (resp.Error ?? "unknown"));
            return;
        }
        _tasks?.ResetStop();
        StatusText = "Task stop reset.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsStore.Changed -= OnSettingsChanged;
        if (Ambient is not null)
        {
            Ambient.RemarkReady -= OnAmbientRemark;
            Ambient.SetConversationBusy(false);
        }
        if (_authority is not null) _authority.StateChanged -= OnAuthorityStateChanged;
        _flushTimer.Dispose();
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        lock (_deltaSync)
        {
            _pendingDelta.Clear();
        }
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Unexpected chat UI failure.")]
    private static partial void LogUnexpectedChatFailure(ILogger logger, Exception exception);
}
