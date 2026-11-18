# Voice wake phrases, expressive speech and chat formatting

Implementation increment: 18 November 2026.

## What works in this version

- Chat now renders a deliberately bounded, **safe** Markdown subset: headings, bold, italic, inline code, fenced code blocks, numbered and bullet lists, paragraphs and labelled HTTP(S) links. It never executes HTML/XAML, downloads image content, opens external links or runs scripts. Right-click **Copy message** to copy the unchanged source text. Streaming updates re-render the current message without accumulating stale formatting.
- Capri's normal chat microphone button can **explicitly start or stop an offline Windows speech recognition session**. Requires both **Voice** and **Enable wake phrases** to be saved in Settings, followed by a separate Yes/No microphone consent dialog for that session.
- Wake phrases: **“Hey Capri”** and **“Capri”**, both accepted case-insensitively, with an offline recognition confidence threshold. Capri then listens for **one separate question** for up to **12 seconds**. That question is sent to the **configured AI provider** using the same chat pipeline as typed messages.
- The existing approved character gestures play **Lean In** on a real recognized wake phrase. A visible **MIC ON** state is shown in chat whenever the mic is in use. Hiding or closing the chat, changing voice settings to disabled, stopping the session or locking Windows stops recognition; lock handling stops the audio engine before UI dispatch.
- Once the AI has answered, a local Windows SAPI female voice can read the reply. Spoken text is shortened and stripped of Markdown symbols, large code blocks and standalone URLs, so it does not narrate source code unnecessarily. Speech rate and volume are configurable.
- **No microphone starts at Windows boot**, opening Capri, opening Settings, clicking the character, or enabling the saved preference alone. No raw mic recordings are written to disk or uploaded by this recognizer.

## Voice fidelity: deliberately not marked complete

The installed machine reports the offline **Microsoft English (UK) Speech Recognizer 8.0**. Installed standard Windows TTS voices include **Microsoft Hazel Desktop** and **Microsoft Zira Desktop**; these are *functional fallbacks*, not neural character-quality voices. We have **not** produced or claimed an exact imitation of Capri's voice from the show.

The official *Cyberpunk: Edgerunners 2* character page credits **Aoi Koga** for Capri's Japanese voice: https://www.cyberpunk.net/en/edgerunners2. Reproducing a specific performance depends on a suitable rights-cleared reference voice/model and real user listening acceptance. A proper natural-sounding original Capri voice should:
- Sound conversational, youthful, agile, emotionally expressive and distinctively digital, **without** repetitive monotone delivery
- Use context-aware stress, deliberate pauses and expressive dynamics while retaining legible speech
- Be locally runnable where practical, with clear speech-device selection and optional processing indicators
- Offer user-selectable language/voice models with **no model downloads or cloud audio processing without explicit opt-in**
- Use an original/approved voice design rather than implying that the basic Windows voice is the actor's performance

The voice synthesis adapter is deliberately isolated from the wake-word and chat logic to allow a licensed **offline neural TTS engine** in a future milestone. That neural engine and authentic voice-performance matching are **not implemented here**.

## Safety and limitations

- Audio recognition uses the installed **Windows desktop speech engine**, not a custom neural wake-word classifier. Microphone device availability, recognition accuracy, false activations, pronunciation and background noise need manual testing on a physical PC.
- The fixed two-phrase grammar and the subsequent dictation grammar require a short pause between wake and question. There is no claim of “Hey Capri, what time is it?” in one utterance yet.
- **Mic listening only runs while the Chat panel is visible and the user has opted in during that session**. This is not a background Windows service. Hiding chat revokes active recognition.
- Raw microphone audio stays local to Windows recognition. The resulting *text* may be sent to a cloud model only if the configured provider's separate cloud-consent guard permits it; the UI explicitly warns about this.
- Voice questions can trigger the same existing chat/intents as typed questions, and **do not grant extra file, camera, task approval or shell execution rights**. Emergency-stop state is untouched.
- Current local SAPI synthesis is not guaranteed smooth, human-like or faithful to the series. Physical acoustic acceptance and any future natural TTS installation are separate gates.

## Manual acceptance checklist

1. In Settings, enable voice and wake phrases, save, reopen Capri chat and click **Mic off**, then confirm consent
2. Verify **MIC ON** is displayed and the Windows recording permission indicator appears
3. Say “Hey Capri”, pause, and ask a simple question; verify transcript, AI answer, avatar cue and local spoken response
4. Repeat with “Capri”, then test wrong names, background noise and low-confidence speech; false activations must be limited
5. Test mic **Stop**, chat hide, Windows lock, voice settings disabled and app exit; microphone capture must cease
6. Test AI/network failure or cancellation; microphone should not be left in an incorrect state
7. Review copied Markdown messages, syntax, links, code and streaming partial tokens; no content executes automatically
8. Judge actual voice quality by listening before calling the sound final

Automated tests validate recognition policy, timing/case, source-code narration stripping, text bounds and formatted message updates, but cannot certify physical mic accuracy or actor-voice fidelity.
