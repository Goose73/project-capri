using System.Speech.Recognition;
using Capri.Core.Voice;

namespace Capri.Desktop.Services;

/// <summary>
/// Explicitly consented local Windows microphone session. Wake grammar and
/// free dictation do NOT compete: dictation is enabled only for the next
/// question, then disabled during/after the response. Supports a single
/// utterance ("Hey Capri, how are you?") when the recognizer supports a
/// wake-prefixed dictation grammar. Does not save audio or transcripts.
/// </summary>
public sealed class WindowsWakeWordSession : IDisposable
{
    private readonly object _sync = new();
    private SpeechRecognitionEngine? _recognizer;
    private Grammar? _wakeGrammar;
    private Grammar? _commandGrammar;
    private Grammar? _questionGrammar;
    private Timer? _questionTimer;
    private bool _waitingForQuestion;
    private bool _responding;
    private DateTimeOffset _armedUntil;
    private DateTimeOffset _lastFeedback;

    public bool IsListening
    {
        get { lock (_sync) return _recognizer is not null; }
    }

    public event Action? WakeDetected;
    public event Action<string>? QuestionDetected;
    /// <summary>Only fixed local status strings; never recognised audio text.</summary>
    public event Action<string>? Feedback;

    public void Start(string? preferredLanguage)
    {
        lock (_sync)
        {
            if (_recognizer is not null) return;
        }

        var options = SpeechRecognitionEngine.InstalledRecognizers();
        var info = options.FirstOrDefault(x =>
            string.Equals(x.Culture.Name, preferredLanguage, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault(x => string.Equals(x.Culture.Name, "en-GB", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No compatible offline Windows speech recognizer is installed.");

        var engine = new SpeechRecognitionEngine(info);
        Grammar? wake = null;
        Grammar? question = null;
        Grammar? command = null;
        Timer? timer = null;
        try
        {
            var wakeBuilder = new GrammarBuilder { Culture = info.Culture };
            wakeBuilder.Append(new Choices("Hey Capri", "Capri"));
            wake = new Grammar(wakeBuilder) { Name = "capri-wake" };
            engine.LoadGrammar(wake);

            question = new DictationGrammar { Name = "capri-question", Enabled = false };
            engine.LoadGrammar(question);

            // One-utterance grammar: wake phrase AND following dictation.
            // Some legacy speech languages do not support combined grammar;
            // two-step wake + question still works in those cases.
            try
            {
                var commandBuilder = new GrammarBuilder { Culture = info.Culture };
                commandBuilder.Append(new Choices("Hey Capri", "Capri"));
                commandBuilder.AppendDictation();
                command = new Grammar(commandBuilder) { Name = "capri-command" };
                engine.LoadGrammar(command);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or
                                       System.Runtime.InteropServices.COMException)
            {
                command = null;
            }

            timer = new Timer(_ => OnQuestionWindowExpired(), null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            engine.SpeechRecognized += OnRecognized;
            engine.SpeechRecognitionRejected += OnRecognitionRejected;
            engine.SpeechDetected += OnSpeechDetected;
            engine.RecognizerUpdateReached += OnRecognizerUpdateReached;
            engine.SetInputToDefaultAudioDevice();

            lock (_sync)
            {
                if (_recognizer is not null)
                    throw new InvalidOperationException("Another microphone session has already started.");

                _recognizer = engine;
                _wakeGrammar = wake;
                _commandGrammar = command;
                _questionGrammar = question;
                _questionTimer = timer;
                _waitingForQuestion = false;
                _responding = false;
                _lastFeedback = DateTimeOffset.MinValue;
            }

            engine.RecognizeAsync(RecognizeMode.Multiple);
            Feedback?.Invoke(command is null
                ? "Microphone ready. Say 'Hey Capri', pause, then ask your question."
                : "Microphone ready. Say 'Hey Capri' followed by your question, or pause between them.");
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_recognizer, engine))
                {
                    _recognizer = null;
                    _wakeGrammar = null;
                    _commandGrammar = null;
                    _questionGrammar = null;
                    _questionTimer = null;
                }
            }
            timer?.Dispose();
            Unsubscribe(engine);
            engine.Dispose();
            throw;
        }
    }

    private void OnSpeechDetected(object? sender, SpeechDetectedEventArgs e)
    {
        bool waiting;
        lock (_sync) waiting = _waitingForQuestion;
        ReportFeedback(waiting
            ? "Mic heard your voice. Listening for your question..."
            : "Mic heard your voice. Listening for 'Hey Capri' or 'Capri'.");
    }

    private void OnRecognitionRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        ReportFeedback("Speech wasn't recognised. Speak clearly, a little closer to the microphone.");
    }

    private void ReportFeedback(string status)
    {
        lock (_sync)
        {
            if (_recognizer is null || _responding) return;
            var now = DateTimeOffset.UtcNow;
            if (now - _lastFeedback < TimeSpan.FromSeconds(3)) return;
            _lastFeedback = now;
        }
        Feedback?.Invoke(status);
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        bool wake = false;
        string? question = null;
        bool updateMode = false;
        SpeechRecognitionEngine? engine;
        var grammarName = e.Result?.Grammar?.Name;
        var text = e.Result?.Text;
        var confidence = e.Result?.Confidence ?? 0f;
        lock (_sync)
        {
            engine = _recognizer;
            if (engine is null || !ReferenceEquals(sender, engine) || _responding)
                return;

            if (!_waitingForQuestion &&
                string.Equals(grammarName, "capri-command", StringComparison.Ordinal))
            {
                question = WakeWordPolicy.ExtractInlineQuestion(text, confidence);
                if (question is not null)
                {
                    _responding = true;
                    _questionTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    wake = true;
                    updateMode = true;
                }
            }
            else if (!_waitingForQuestion &&
                     string.Equals(grammarName, "capri-wake", StringComparison.Ordinal) &&
                     WakeWordPolicy.IsWakePhrase(text, confidence))
            {
                _waitingForQuestion = true;
                _armedUntil = DateTimeOffset.UtcNow.AddSeconds(12);
                _questionTimer?.Change(TimeSpan.FromSeconds(12), Timeout.InfiniteTimeSpan);
                wake = true;
                updateMode = true;
            }
            else if (_waitingForQuestion &&
                     string.Equals(grammarName, "capri-question", StringComparison.Ordinal))
            {
                question = WakeWordPolicy.AcceptQuestion(text, confidence,
                    armed: true, _armedUntil, DateTimeOffset.UtcNow);
                if (question is not null)
                {
                    _waitingForQuestion = false;
                    _responding = true;
                    _questionTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    updateMode = true;
                }
            }
        }

        if (updateMode)
            RequestGrammarSwitch(engine);
        if (wake) WakeDetected?.Invoke();
        if (question is not null) QuestionDetected?.Invoke(question);
    }

    private void OnRecognizerUpdateReached(object? sender, RecognizerUpdateReachedEventArgs e)
    {
        lock (_sync)
        {
            if (_recognizer is null || !ReferenceEquals(sender, _recognizer)) return;
            // This callback is the documented safe point for enabling and
            // disabling grammars during continuous recognition.
            var waiting = _waitingForQuestion && !_responding;
            if (_wakeGrammar is not null) _wakeGrammar.Enabled = !waiting;
            if (_commandGrammar is not null) _commandGrammar.Enabled = !waiting;
            if (_questionGrammar is not null) _questionGrammar.Enabled = waiting;
        }
    }

    private static void RequestGrammarSwitch(SpeechRecognitionEngine? engine)
    {
        if (engine is null) return;
        try { engine.RequestRecognizerUpdate(); }
        catch (InvalidOperationException) { /* Session may be stopping. */ }
    }

    private void OnQuestionWindowExpired()
    {
        SpeechRecognitionEngine? engine;
        lock (_sync)
        {
            if (_recognizer is null || !_waitingForQuestion || _responding)
                return;
            _waitingForQuestion = false;
            engine = _recognizer;
        }
        RequestGrammarSwitch(engine);
        Feedback?.Invoke("No question heard. Say 'Hey Capri' to try again.");
    }

    /// <summary>Ignore assistant speech while answering; return to wake mode afterwards.</summary>
    public void FinishResponse()
    {
        SpeechRecognitionEngine? engine;
        lock (_sync)
        {
            _responding = false;
            _waitingForQuestion = false;
            _questionTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            engine = _recognizer;
        }
        RequestGrammarSwitch(engine);
    }

    public void Stop()
    {
        SpeechRecognitionEngine? engine;
        Timer? timer;
        lock (_sync)
        {
            engine = _recognizer;
            timer = _questionTimer;
            _recognizer = null;
            _wakeGrammar = null;
            _commandGrammar = null;
            _questionGrammar = null;
            _questionTimer = null;
            _waitingForQuestion = false;
            _responding = false;
        }

        timer?.Dispose();
        if (engine is null) return;
        Unsubscribe(engine);
        try { engine.RecognizeAsyncCancel(); }
        catch (InvalidOperationException) { }
        finally { engine.Dispose(); }
    }

    private void Unsubscribe(SpeechRecognitionEngine engine)
    {
        engine.SpeechRecognized -= OnRecognized;
        engine.SpeechRecognitionRejected -= OnRecognitionRejected;
        engine.SpeechDetected -= OnSpeechDetected;
        engine.RecognizerUpdateReached -= OnRecognizerUpdateReached;
    }

    public void Dispose() => Stop();
}
