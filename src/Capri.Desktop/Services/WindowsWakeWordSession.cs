using System.Speech.Recognition;
using Capri.Core.Voice;

namespace Capri.Desktop.Services;

/// <summary>
/// Per-window offline Windows speech recognition. Only user-action Start()
/// enables the microphone. Nothing is saved or sent over the network. After
/// "Hey Capri" or "Capri", the next separate utterance is a question. Mic
/// remains visibly on until Stop or window hide, and voice results cannot
/// change task safety approvals.
/// </summary>
public sealed class WindowsWakeWordSession : IDisposable
{
    private SpeechRecognitionEngine? _recognizer;
    private bool _waitingForQuestion;
    private bool _responding;
    private DateTimeOffset _armedUntil;
    private readonly object _sync = new();

    public bool IsListening => _recognizer is not null;

    public event Action? WakeDetected;
    public event Action<string>? QuestionDetected;

    public void Start(string? preferredLanguage)
    {
        if (_recognizer is not null)
            return;
        var options = SpeechRecognitionEngine.InstalledRecognizers();
        var preferred = options.FirstOrDefault(x =>
            string.Equals(x.Culture.Name, preferredLanguage, StringComparison.OrdinalIgnoreCase));
        var gb = options.FirstOrDefault(x =>
            string.Equals(x.Culture.Name, "en-GB", StringComparison.OrdinalIgnoreCase));
        var info = preferred ?? gb ??
            throw new InvalidOperationException(
                "No compatible offline Windows speech recognizer is installed.");

        var engine = new SpeechRecognitionEngine(info);
        try
        {
            var phrases = new GrammarBuilder { Culture = info.Culture };
            phrases.Append(new Choices("Hey Capri", "Capri"));
            engine.LoadGrammar(new Grammar(phrases) { Name = "capri-wake" });
            engine.LoadGrammar(new DictationGrammar { Name = "capri-question" });
            engine.SpeechRecognized += OnRecognized;
            engine.SetInputToDefaultAudioDevice();
            engine.RecognizeAsync(RecognizeMode.Multiple);
            lock (_sync)
            {
                _recognizer = engine;
                _waitingForQuestion = false;
                _responding = false;
            }
        }
        catch
        {
            engine.SpeechRecognized -= OnRecognized;
            engine.Dispose();
            throw;
        }
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        bool awaken = false;
        string? question = null;
        lock (_sync)
        {
            if (_recognizer is null || _responding) return;
            var text = e.Result?.Text;
            var confidence = e.Result?.Confidence ?? 0f;
            if (WakeWordPolicy.IsWakePhrase(text, confidence))
            {
                _waitingForQuestion = true;
                _armedUntil = DateTimeOffset.UtcNow.AddSeconds(12);
                awaken = true;
            }
            else
            {
                question = WakeWordPolicy.AcceptQuestion(
                    text, confidence, _waitingForQuestion, _armedUntil, DateTimeOffset.UtcNow);
                if (question is not null)
                {
                    _waitingForQuestion = false;
                    _responding = true;
                }
            }
        }

        if (awaken) WakeDetected?.Invoke();
        if (question is not null) QuestionDetected?.Invoke(question);
    }

    /// <summary>
    /// Ignore audio while the response is being processed or spoken, so
    /// Capri's own voice cannot trigger a second question.
    /// </summary>
    public void FinishResponse()
    {
        lock (_sync)
        {
            _responding = false;
            _waitingForQuestion = false;
        }
    }

    public void Stop()
    {
        SpeechRecognitionEngine? engine;
        lock (_sync)
        {
            engine = _recognizer;
            _recognizer = null;
            _waitingForQuestion = false;
            _responding = false;
        }
        if (engine is null) return;
        engine.SpeechRecognized -= OnRecognized;
        try { engine.RecognizeAsyncCancel(); }
        catch (InvalidOperationException) { }
        finally { engine.Dispose(); }
    }

    public void Dispose() => Stop();
}
