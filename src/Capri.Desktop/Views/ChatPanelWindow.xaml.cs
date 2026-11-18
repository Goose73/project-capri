using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Capri.Core.Abstractions;
using Capri.Core.Avatar;
using Capri.Core.Models;
using Capri.Desktop.Interop;
using Capri.Desktop.Services;
using Capri.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Capri.Desktop.Views;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1001",
    Justification = "WPF closes the mic session and TTS engine explicitly in its Closed handler.")]
public partial class ChatPanelWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly OverlayChatViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly AmbientCompanionService _ambient;
    private readonly IAppSettingsStore _settingsStore;
    private readonly WindowsWakeWordSession _wake = new();
    private readonly WindowsVoiceReply _voiceReply = new();
    private CancellationTokenSource? _voiceResponseCts;
    private int _voiceEpoch;
    private bool _allowClose;

    public ChatPanelWindow(OverlayChatViewModel viewModel, IServiceProvider serviceProvider)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _ambient = serviceProvider.GetRequiredService<AmbientCompanionService>();
        _settingsStore = serviceProvider.GetRequiredService<IAppSettingsStore>();

        InitializeComponent();
        _wake.WakeDetected += OnVoiceWakeDetected;
        _wake.QuestionDetected += OnVoiceQuestionDetected;
        _settingsStore.Changed += OnVoiceSettingsChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnWindowsSessionSwitch;
        DataContext = _viewModel;

        SourceInitialized += OnSourceInitialized;
        _viewModel.Messages.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(ScrollMessagesToEnd);
        _viewModel.UserTextSubmitted += OnTypedUserTextSubmitted;
        Closed += (_, _) =>
        {
            StopVoiceSession();
            _wake.WakeDetected -= OnVoiceWakeDetected;
            _wake.QuestionDetected -= OnVoiceQuestionDetected;
            _settingsStore.Changed -= OnVoiceSettingsChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnWindowsSessionSwitch;
            _wake.Dispose();
            _viewModel.UserTextSubmitted -= OnTypedUserTextSubmitted;
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try { InputBox.Focus(); } catch { /* focus is best-effort */ }
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
            else
            {
                // A hidden chat must never keep recording speech.
                StopVoiceSession();
            }
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                _ = SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            }
        }
        catch
        {
            // Non-fatal.
        }
    }

    /// <summary>Positions the chat panel beside the character window, clamped to the character's monitor work area.</summary>
    public void RepositionNear(Window characterWindow)
    {
        if (characterWindow is null)
        {
            return;
        }

        try
        {
            var workArea = MonitorWorkArea.GetWorkAreaDip(characterWindow);
            var gap = 8.0;
            var leftCandidate = characterWindow.Left - Width - gap;
            var rightCandidate = characterWindow.Left + characterWindow.Width + gap;

            var leftFits = leftCandidate >= workArea.Left - 0.5;
            var rightFits = rightCandidate + Width <= workArea.Right + 0.5;
            var desiredLeft = leftFits ? leftCandidate : (rightFits ? rightCandidate : leftCandidate);

            var desiredTop = characterWindow.Top + characterWindow.Height - Height;
            var maxLeft = Math.Max(workArea.Left, workArea.Right - Width);
            var maxTop = Math.Max(workArea.Top, workArea.Bottom - Height);

            Left = Math.Min(Math.Max(workArea.Left, desiredLeft), maxLeft);
            Top = Math.Min(Math.Max(workArea.Top, desiredTop), maxTop);

            // If clamped position would overlap the character badly, try the other side.
            var overlapsCharacter = Left < characterWindow.Left + characterWindow.Width &&
                                    Left + Width > characterWindow.Left &&
                                    Top < characterWindow.Top + characterWindow.Height &&
                                    Top + Height > characterWindow.Top;
            if (overlapsCharacter)
            {
                var alternate = Math.Abs(desiredLeft - leftCandidate) < 0.1 ? rightCandidate : leftCandidate;
                Left = Math.Min(Math.Max(workArea.Left, alternate), maxLeft);
            }
        }
        catch
        {
            // Best-effort positioning.
        }
    }

    private void ScrollMessagesToEnd()
    {
        try
        {
            MessagesScroll.ScrollToEnd();
        }
        catch
        {
            // Non-fatal UI nicety.
        }
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            e.Handled = true;
            if (_viewModel.SendCommand.CanExecute(null))
            {
                _viewModel.SendCommand.Execute(null);
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    private void OnTypedUserTextSubmitted(string text)
    {
        // Only an actual typed message triggers this. There is no live
        // microphone wake-word listener or screen-capture permission here.
        var gesture = AvatarGestureTextCueParser.Parse(text);
        if (gesture is not { } cue) return;

        if (Owner is CharacterWindow character)
            character.QueueUserGesture(cue);

        if (cue == AvatarGesture.TurnToScreen)
        {
            _viewModel.Messages.Add(new ChatMessageViewModel(
                "System",
                "Capri can turn toward the screen, but screen capture/inspection " +
                "is not connected. No screen content was accessed."));
        }
    }

    private void DeveloperWorkspace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = _serviceProvider.GetRequiredService<DeveloperWorkspaceWindow>();
            window.Owner = IsVisible ? this : (Owner?.IsVisible == true ? Owner : null);
            window.ShowDialog();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this,
                "Developer workspace could not be opened. No project files were accessed.",
                "Capri", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Explicit, user-initiated recovery for a configured local Ollama server.
    /// Never run arbitrary user-supplied executable paths or silently launch AI
    /// just because a model request failed.
    /// </summary>
    private async void StartOllama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = _serviceProvider
                .GetRequiredService<Capri.Core.Abstractions.IAppSettingsStore>()
                .Current.Provider;
            if (!string.Equals(settings.ProviderId, "ollama", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttp ||
                endpoint.Port != 11434 ||
                !(string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                  (IPAddress.TryParse(endpoint.Host, out var hostIp) && IPAddress.IsLoopback(hostIp))))
            {
                MessageBox.Show(this,
                    "The currently selected AI provider is not local Ollama on port 11434. " +
                    "Check your model connection in Settings.",
                    "Capri - local AI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            const string healthUrl = "http://127.0.0.1:11434/api/tags";
            if (await IsOllamaReadyAsync(client, healthUrl).ConfigureAwait(true))
            {
                _viewModel.StatusText = "Local Ollama is already running.";
                return;
            }

            var executable = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama.exe");
            if (!File.Exists(executable))
            {
                MessageBox.Show(this,
                    "Ollama is not installed in its normal user folder. " +
                    "Install Ollama or launch the installed version manually.",
                    "Capri - local AI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "serve",
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            _viewModel.StatusText = "Starting local Ollama...";
            for (var attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(500).ConfigureAwait(true);
                if (await IsOllamaReadyAsync(client, healthUrl).ConfigureAwait(true))
                {
                    _viewModel.StatusText = "Local AI ready. You can send a message.";
                    return;
                }
            }
            _viewModel.StatusText = "Ollama did not become ready. Check whether it is running.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.ComponentModel.Win32Exception or HttpRequestException or
                                   InvalidOperationException or TaskCanceledException)
        {
            _viewModel.StatusText = "Could not start Ollama. Try launching Ollama from Windows.";
        }
    }

    private static async Task<bool> IsOllamaReadyAsync(HttpClient client, string url)
    {
        try
        {
            using var response = await client.GetAsync(url).ConfigureAwait(true);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private async void StartCamera_Click(object sender, RoutedEventArgs e)
    {
        if (_ambient.IsObserving) return;
        var settingsStore = _serviceProvider.GetRequiredService<Capri.Core.Abstractions.IAppSettingsStore>();
        if (!settingsStore.Current.AmbientVision.Enabled)
        {
            MessageBox.Show(this,
                "Enable Ambient Companion webcam awareness in Settings, then save. " +
                "The preference alone never activates your camera.",
                "Capri — camera is off", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            "Allow Capri to periodically capture images from your webcam during THIS session?\n\n" +
            "Snapshots are analysed only by Ollama on this PC (127.0.0.1). No images are " +
            "saved, recorded, or sent to cloud providers. A CAM ON marker remains visible " +
            "on the Capri character; Pause and Stop are available here. If enabled in " +
            "Settings, Capri may occasionally comment or speak through local Windows speech.\n\n" +
            "Please avoid including other people in the camera view without their knowledge. " +
            "Hiding Capri, closing the app, or locking Windows stops the session.\n\nStart webcam awareness?",
            "Capri — webcam consent", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try { await _ambient.StartSessionAsync(confirmed: true).ConfigureAwait(true); }
        catch (Exception)
        {
            MessageBox.Show(this, "Could not start camera awareness. Check local Ollama and Windows camera permissions.",
                "Capri", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PauseCamera_Click(object sender, RoutedEventArgs e) => _ambient.Pause();

    private async void ResumeCamera_Click(object sender, RoutedEventArgs e)
    {
        try { await _ambient.ResumeAsync().ConfigureAwait(true); }
        catch (Exception) { _ambient.Stop(); }
    }

    private void StopCamera_Click(object sender, RoutedEventArgs e) => _ambient.Stop();

    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_wake.IsListening)
        {
            StopVoiceSession();
            return;
        }

        var voice = _settingsStore.Current.Voice;
        if (!voice.Enabled || !voice.EnableWakeWord)
        {
            MessageBox.Show(this,
                "Enable Voice and wake phrases in Capri Settings first. " +
                "These preferences never activate your microphone automatically.",
                "Capri - voice preferences", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var permission = MessageBox.Show(this,
            "Allow Capri to use the DEFAULT Windows microphone in this chat session?\n\n" +
            "The offline Windows recognizer listens for 'Hey Capri' or 'Capri', " +
            "then captures your next spoken question. Raw audio is not saved or uploaded. " +
            "Recognised question TEXT is sent to your configured AI provider, " +
            "subject to its cloud/local consent settings.\n\n" +
            "MIC ON stays visible while listening. Press Stop mic, hide chat, " +
            "close Capri, or lock Windows to stop.\n\n" +
            "Windows' built-in reply voice is a basic fallback, not the anime actress's voice.\n\n" +
            "Start this microphone session?",
            "Capri - microphone consent", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No);
        if (permission != MessageBoxResult.Yes) return;

        try
        {
            _wake.Start(voice.Language);
            Interlocked.Increment(ref _voiceEpoch);
            MicButton.Content = "Stop mic";
            VoiceIndicator.Text = "MIC ON";
            _viewModel.StatusText = "Mic listening offline. Say 'Hey Capri' or 'Capri'.";
        }
        catch (Exception)
        {
            StopVoiceSession();
            MessageBox.Show(this,
                "Could not open the offline Windows microphone recognizer. " +
                "Check microphone access, default recording device and installed speech languages.",
                "Capri - microphone unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnVoiceWakeDetected()
    {
        var epoch = Volatile.Read(ref _voiceEpoch);
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (epoch != Volatile.Read(ref _voiceEpoch) ||
                !_wake.IsListening || !IsVisible) return;
            VoiceIndicator.Text = "MIC ON · LISTENING";
            _viewModel.StatusText = "Capri heard you. Ask your question now.";
            if (Owner is CharacterWindow character)
                character.QueueUserGesture(AvatarGesture.LeanIn);
        }));
    }

    private void OnVoiceQuestionDetected(string question)
    {
        var epoch = Volatile.Read(ref _voiceEpoch);
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            // An old recognition event must NEVER be submitted after the
            // user ended a session and started another.
            if (epoch == Volatile.Read(ref _voiceEpoch))
                _ = AnswerVoiceQuestionAsync(question);
        }));
    }

    private async Task AnswerVoiceQuestionAsync(string question)
    {
        if (!_wake.IsListening || !IsVisible) return;
        var cts = new CancellationTokenSource();
        _voiceResponseCts = cts;
        VoiceIndicator.Text = "MIC ON · RESPONDING";
        _viewModel.StatusText = "Voice question recognised; asking Capri...";
        try
        {
            var answer = await _viewModel.AskByVoiceAsync(question).ConfigureAwait(true);
            if (cts.IsCancellationRequested || !_wake.IsListening) return;
            if (!string.IsNullOrWhiteSpace(answer) && _settingsStore.Current.Voice.Enabled)
            {
                VoiceIndicator.Text = "MIC ON · SPEAKING";
                await _voiceReply.SpeakAsync(answer, _settingsStore.Current.Voice, cts.Token)
                    .ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // The user stopped the microphone session.
        }
        catch (Exception)
        {
            _viewModel.StatusText = "Voice reply unavailable; the text response remains in chat.";
        }
        finally
        {
            if (ReferenceEquals(_voiceResponseCts, cts))
            {
                _voiceResponseCts = null;
                _wake.FinishResponse();
                if (_wake.IsListening)
                {
                    VoiceIndicator.Text = "MIC ON";
                    MicButton.Content = "Stop mic";
                }
            }
            cts.Dispose();
        }
    }

    private void StopVoiceSession()
    {
        Interlocked.Increment(ref _voiceEpoch);
        var cts = _voiceResponseCts;
        _voiceResponseCts = null;
        if (cts is not null)
        {
            cts.Cancel();
            if (_viewModel.CancelCommand.CanExecute(null))
                _viewModel.CancelCommand.Execute(null);
        }

        _voiceReply.Stop();
        try { _wake.Stop(); }
        catch (Exception) { /* A failed driver shutdown must never strand the window. */ }
        VoiceIndicator.Text = "MIC OFF";
        MicButton.Content = "Mic off";
    }

    private void OnVoiceSettingsChanged(object? sender, AppSettingsChangedEventArgs e)
    {
        if (e.Current.Voice.Enabled && e.Current.Voice.EnableWakeWord) return;
        _ = Dispatcher.BeginInvoke(new Action(StopVoiceSession));
    }

    private void OnWindowsSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or
            Microsoft.Win32.SessionSwitchReason.SessionLogoff or
            Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect)
        {
            // Stop the audio engine synchronously before UI dispatch can be
            // delayed by the workstation lock or desktop switch.
            try { _wake.Stop(); }
            catch (Exception) { }
            _ = Dispatcher.BeginInvoke(new Action(StopVoiceSession));
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    public void AllowShutdownClose()
    {
        _allowClose = true;
    }

    public async Task OpenSettingsAsync()
    {
        try
        {
            var window = _serviceProvider.GetRequiredService<SettingsWindow>();
            window.Owner = IsVisible ? this : (Owner?.IsVisible == true ? Owner : null);
            await window.InitializeAsync();
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Settings could not be opened. Check logs for details.",
                "Capri",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
