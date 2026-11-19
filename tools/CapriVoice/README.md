# Capri local expressive voice

Optional, *offline* voice replies for the Windows companion using
[Kokoro ONNX](https://github.com/thewh1teagle/kokoro-onnx). This supplies a
synthetic, youthful expressive female timbre; it is **not** a clone or
recording of any anime voice actor.

## Setup on Windows

Install [uv](https://docs.astral.sh/uv/) in your user profile, then:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\CapriVoice\setup.ps1
```

The setup script creates `%LOCALAPPDATA%\Capri\Voice\venv` and downloads
only two pinned, SHA-256-verified assets from the official Kokoro ONNX
release. It does **not** activate the mic, change your existing Capri
settings or run a background server automatically. Setup requires internet;
synthesis is fully local afterwards.

In Capri **Settings → Voice / mic**, enable **Use natural local neural
voice** and choose `af_heart` as the **Voice name**. The address must remain
`http://127.0.0.1:5000`. Save. Capri will start the already-installed
voice engine when it needs to narrate an answer. Nothing starts from merely
enabling the microphone or visiting Settings. The engine never downloads
voices on demand, and only listens on loopback. It has no microphone access.

### Presets

- `af_heart`: warm, bright US-English synthetic female voice (recommended)
- `af_bella`: US-English synthetic female alternative
- `af_sky` or `af_nova`: lighter US-English choices
- `bf_emma` or `bf_isabella`: British-English choices

Capri forwards the selected name to the local `/synthesize` endpoint,
and keeps custom Piper server names working. A blank name leaves the server
default. Pacing and output volume can be adjusted through the existing
Settings sliders. Set Use natural local neural voice to OFF for Windows SAPI.

Click the chat **More controls → Preview Capri voice** to hear an original
sample without opening the microphone or asking the AI. For A/B comparisons,
run `smoke_test.py` inside the local Python venv: it creates sample WAVs
under the private `%LOCALAPPDATA%\Capri\Voice\previews` directory.
These WAVs contain only the fixed text in the test, no user content.

### Implementation and privacy

The service accepts a bounded JSON `POST /synthesize` request and returns
WAV, compatible with Capri's existing Piper adapter. It supports
`GET /health` for diagnostics. It rejects arbitrary paths, oversized
requests, unknown voices and non-finite parameters; the localhost-only
server does not record transcripts, persist prompts or upload speech.
If the local service is absent or unavailable, Capri falls back to the
installed Windows voice and reports that fallback. No external speech
provider or paid API is required.

Kokoro-82M model/voice weights are Apache-2.0; the `kokoro-onnx` wrapper
is MIT. Review upstream voice-model terms before redistributing. Assets
are not committed to the public Capri repository. The server is ordinary
Python source that runs entirely locally.

Validated on Windows with three synthetic voice previews, a live
`/synthesize` WAV protocol test, and .NET tests. Human listening and
comparison with the desired character timbre remain separate acceptance
steps.
