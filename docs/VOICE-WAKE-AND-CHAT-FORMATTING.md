# Voice wake phrases, expressive speech and chat formatting

Implementation increments: 18–19 November 2026.

## What works in this version

- Chat now renders a deliberately bounded, **safe** Markdown subset: headings, bold, italic, inline code, fenced code blocks, numbered and bullet lists, paragraphs and labelled HTTP(S) links. It never executes HTML/XAML, downloads image content, opens external links or runs scripts. Right-click **Copy message** to copy the unchanged source text. Streaming updates re-render the current message without accumulating stale formatting.
- Capri's normal chat microphone button can **explicitly start or stop an offline Windows speech recognition session**. Requires both **Voice** and **Enable wake phrases** to be saved in Settings, followed by a separate Yes/No microphone consent dialog for that session.
- Wake phrases: **“Hey Capri”** and **“Capri”**, both accepted case-insensitively, with an offline recognition confidence threshold. Capri accepts a **single utterance** (for example “Hey Capri, what time is it?”) or listens for a **separate question** for up to **12 seconds** after the wake phrase. The question is sent to the configured AI provider with the same policy as typed messages.
- The existing approved character gestures play **Lean In** on a real recognized wake phrase. A visible **MIC ON** state is shown in chat and as a persistent badge on the avatar whenever the mic is in use. The explicitly approved session continues if only Chat is hidden, and a wake opens Chat again. Hiding the avatar, disabling voice settings, pressing Stop mic, closing Capri or locking Windows stops recognition; lock handling stops the audio engine before UI dispatch.
- Once the AI has answered, a local Windows SAPI female voice can read the reply. Spoken text is shortened and stripped of Markdown symbols, large code blocks and standalone URLs, so it does not narrate source code unnecessarily. Speech rate and volume are configurable.
- **No microphone starts at Windows boot**, opening Capri, opening Settings, clicking the character, or enabling the saved preference alone. No raw mic recordings are written to disk or uploaded by this recognizer.

## Wake-word troubleshooting (19 November 2026)

**Voice enabled is not the same thing as MIC ON.** Right-click the Capri avatar and choose **Start listening (Hey Capri)...**, or open Chat and click **Start mic**; then approve the explicit microphone prompt. The MIC ON badge appears on the avatar and Chat header. You may close just the Chat panel and keep listening while the avatar is visible. Say “Hey Capri” to reopen Chat. Right-click the avatar and choose **Stop listening** (or click Stop mic) to end recognition. Hiding the avatar, locking Windows or exiting Capri always ends the session. No mic auto-start at boot.

You can say **“Hey Capri”**, pause briefly, then say your question—or try a single sentence such as **“Hey Capri, how are you?”**. If the mic detects audio but the recognizer rejects it, the Chat status now explains that speech was detected but not understood. Verify the correct Windows default recording device and desktop app microphone permissions.

On the development PC, the default input was **TONOR TC-777**, microphone permission was allowed, and an **English (UK)** offline recognizer was installed. Capri does not automatically start the mic at boot or silently record audio. Listening behind the Chat panel is opt-in per app session and remains visible on the avatar; no invisible background Windows service is created.

## Voice fidelity: deliberately not marked complete

The installed machine reports the offline **Microsoft English (UK) Speech Recognizer 8.0**. Installed standard Windows TTS voices include **Microsoft Hazel Desktop** and **Microsoft Zira Desktop**; these are *functional fallbacks*, not neural character-quality voices. We have **not** produced or claimed an exact imitation of Capri's voice from the show.

The official *Cyberpunk: Edgerunners 2* character page credits **Aoi Koga** for Capri's Japanese voice: https://www.cyberpunk.net/en/edgerunners2. Reproducing a specific performance depends on a suitable rights-cleared reference voice/model and real user listening acceptance. A proper natural-sounding original Capri voice should:
- Sound conversational, youthful, agile, emotionally expressive and distinctively digital, **without** repetitive monotone delivery
- Use context-aware stress, deliberate pauses and expressive dynamics while retaining legible speech
- Be locally runnable where practical, with clear speech-device selection and optional processing indicators
- Offer user-selectable language/voice models with **no model downloads or cloud audio processing without explicit opt-in**
- Use an original/approved voice design rather than implying that the basic Windows voice is the actor's performance

### Optional local neural voice (Piper)

Capri now has an **optional Piper neural TTS adapter** for a locally installed and already-running Piper HTTP server. It remains OFF by default. In **Settings → Voice / mic**, enable **Prefer an installed Piper neural voice**, and configure a **literal loopback address** (default `http://127.0.0.1:5000`). Capri POSTs only the shortened spoken answer to `/synthesize`, validates the returned bounded WAV and plays it locally. No remote HTTP addresses, hostnames, redirects or proxies are accepted. If the server is unavailable, Capri reports the fallback and speaks through Windows SAPI.

Piper installation/model selection is a separate user action. From the documented official package, after selecting a licence-compatible voice:

```powershell
py -m pip install "piper-tts[http]"
py -m piper.download_voices <approved-voice-id>
py -m piper.http_server -m <approved-voice-id> --host 127.0.0.1 --port 5000
```

These are **manual reference commands**, not operations Capri executes. Review the voice model's licence before choosing it. Piper can produce more natural sound than standard SAPI, but it is not an exact reproduction of the character's Japanese performer or any actor. Authentic vocal identity, performance-directed emotion, model rights and physical listening quality remain unverified.

Official reference: https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/API_HTTP.md

The older Windows voice stays as a fallback and does not require Piper. No voice package or model is downloaded automatically.

## Safety and limitations

- Audio recognition uses the installed **Windows desktop speech engine**, not a custom neural wake-word classifier. Microphone device availability, recognition accuracy, false activations, pronunciation and background noise need manual testing on a physical PC.
- The recognizer listens for the exact wake phrases without running broad dictation in parallel. The separate question grammar activates for 12 seconds after a confirmed wake. An additional optional wake-prefixed grammar supports “Hey Capri, what time is it?” in one utterance where the installed Windows speech language supports it. Both modes were tested by simulated speech recognition on the installed en-GB desktop recognizer; real live-mic accuracy still needs testing.
- **Mic listening requires explicit consent and a visible avatar**. Hiding only Chat allows recognition to continue with a persistent MIC ON avatar badge and an accessible Stop listening menu. Hiding the avatar, locking Windows and app exit revoke capture. This is not a background Windows service.
- Raw microphone audio stays local to Windows recognition. The resulting *text* may be sent to a cloud model only if the configured provider's separate cloud-consent guard permits it; the UI explicitly warns about this.
- Voice questions can trigger the same existing chat/intents as typed questions, and **do not grant extra file, camera, task approval or shell execution rights**. Emergency-stop state is untouched.
- Windows SAPI synthesis is not guaranteed smooth or faithful to the series. The Piper adapter can use an appropriately licensed neural voice only once the user installs, starts and opts into it. Neither backend is guaranteed to reproduce the show's voice; physical acoustic acceptance remains separate.

## Manual acceptance checklist

1. In Settings, enable voice and wake phrases, save; right-click the avatar and choose **Start listening (Hey Capri)...** (or click **Start mic** in Chat); confirm consent
2. Verify **MIC ON** appears on both avatar and Chat and the Windows recording permission indicator appears
3. Say “Hey Capri”, pause, and ask a simple question; verify transcript, AI answer, avatar cue and local spoken response
4. Repeat with “Capri”, then test wrong names, background noise and low-confidence speech; false activations must be limited
5. Hide only Chat and verify the avatar MIC ON badge remains; say the wake phrase to reopen Chat. Stop listening from the avatar menu, then verify hiding the avatar, Windows lock, disabling voice settings and app exit cease capture
6. Test AI/network failure or cancellation; microphone should not be left in an incorrect state
7. Review copied Markdown messages, syntax, links, code and streaming partial tokens; no content executes automatically
8. Judge actual voice quality by listening before calling the sound final

Automated tests validate recognition policy, timing/case, source-code narration stripping, text bounds and formatted message updates, but cannot certify physical mic accuracy or actor-voice fidelity.
