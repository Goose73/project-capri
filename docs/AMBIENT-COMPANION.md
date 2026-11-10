# Ambient Companion — opt-in local webcam awareness

**Status: functional Windows source implementation; CI verification and real-device acceptance required.** Camera access stays **OFF by default**. Nothing is activated just because the preference is saved; the user must start each application session explicitly.

## What this feature does

Capri can occasionally sample a webcam frame while the user games, works or relaxes, interpret it with a **locally installed Ollama vision model**, and optionally leave a short conversational comment. With separate permission she can speak those comments using local Windows speech synthesis. This feature does **not** analyse the game window or desktop pixels: a webcam sees the user and environment, not the screen contents. Screen capture remains a separate permission and integration.

## Using the Windows build

1. Install / start Ollama on the same PC (standard loopback address `127.0.0.1:11434`). Install a compatible vision model, such as `ollama pull qwen3.5:4b`. Verify it can answer an image question locally. This feature does not automatically download models.
2. Connect a webcam and allow desktop apps to use it in Windows **Settings → Privacy & security → Camera**. Close apps that have exclusive camera access.
3. Open **Capri → Settings → Ambient companion / webcam**. Enable webcam awareness, choose a camera index (0 by default), and enter the installed vision model ID. Choose a snapshot interval (20 seconds by default) and optional spontaneous comments or optional offline spoken remarks. **Save**.
4. Open Capri's chat panel. Press **Start camera** and read/accept the dedicated consent dialog for this session. If the local model is unavailable, the camera will not start.
5. A **CAM ON** indicator appears on Capri's floating character while the session is active. Use **Pause**, **Resume** or **Stop** in the chat panel. Hiding Capri, locking or logging off Windows, disabling the preference, changing any camera settings or quitting Capri stops/revokes the session.
6. To use a different camera, change the index in Settings, save, and start a new session. Unsupported device/permission errors stop the session instead of endlessly retrying.

**Consent applies to your current session only.** No background recording, auto-start, Windows service camera capture, cloud upload, or unattended startup is implemented. Be mindful of other people in the camera's view.

## Implementation details

- `AmbientCompanionService` is registered only with the desktop application and **does not start** in its constructor or at WPF host startup. It requires the user-initiated Start button and a second confirmation, plus the saved opt-in.
- `OpenCvAmbientCameraCapture` uses Windows DirectShow via `OpenCvSharp4.Windows`, opens the selected camera for a *single* bounded 640×480-or-smaller JPEG snapshot and then disposes the capture. No video files or webcam history are written.
- `LocalOllamaAmbientVision` sends an in-memory Base64 JPEG only to `http://127.0.0.1:11434/api/chat`. It disables HTTP proxies and redirects and never uses the selected cloud chat provider. It checks `/api/tags` for an exact locally installed model name before activating the session. Requests and responses have size limits, and prompts constrain output to benign, concise comments with no sensitive personal inferences.
- `AmbientVisionPolicy` enforces consent, camera access, visible indicator, local model, no lock/pause/overlap, minimum 10-second sampling and minimum 120-second spontaneous-remark cooldown (defaults 20 seconds and 300 seconds). The model still has to return a new meaningful observation, and the user must not already be chatting.
- `OverlayChatViewModel` receives only a short remark, not camera images, and does not dispatch tasks from webcam data. `WindowsAmbientCommentSpeech` uses local Windows SAPI and only runs when **Speak remarks aloud** is separately enabled.
- In-memory frame bytes are erased after the inference attempt. The JSON request temporarily holds a Base64 copy in managed memory until collected; **no persistent copy** is deliberately created by Capri. Applications and drivers beyond Capri may have their own caching behaviours.
- Stop, pause, changed permissions/preferences, and Windows session-lock events cancel future capture and outstanding cancellable work. A camera driver call already in progress may take a moment to release; test this with real hardware.
- On any capture/model error, Capri stops the session and requires manual restart rather than silently recapturing forever. No error logs include image contents.
- This feature uses NuGet packages `OpenCvSharp4.Windows` and `System.Speech` for video and local speech. Their native components must be present in the distributed Windows build.

## Current limitations and checks

- Device **indexes**, not friendly camera names. No live preview yet (only visible ON state and short sampling feedback). A device selection/preview UI is a future enhancement.
- Actual GPU latency, voice availability, microphone interaction, webcam permissions, native library deployment, device sharing, and game FPS impact require **real Windows PC/webcam acceptance**, which GitHub CI cannot simulate.
- No identity recognition, mood diagnosis, surveillance, face storage, continuous frame-rate video inference, screen reading, or cloud fallback.
- Model outputs are probabilistic and may be inaccurate. The prompt tries to avoid intrusive/incorrect remarks; tests and user evaluation remain necessary.
- The WPF sprite animation branch is separate and is **not** automatically merged into this change. Future events may animate camera awareness after real capture, but that must not falsely imply seeing the game screen.
- Windows sleep/session state and Stop should be tested while the webcam is in use. Privacy-conscious users may additionally use a camera shutter.

## Next work

1. Test Start/Stop/Pause/Resume with physical webcam, Windows permissions, and a downloaded vision model. Check OpenCV native runtime on clean Windows installation.
2. Add friendly webcam picker, optional user-visible preview, audio voice selection, quiet hours, and CPU/GPU adaptive backoff.
3. Integrate verified successful observations with Capri's expressive animation cues, subject to reduced-motion and no unsupported claims of screen visibility.
4. Add camera lifecycle stress tests, performance instrumentation without frame logging, and optional multi-person detection without identifying people.
5. Test companion behaviour while gaming, coding, and idle to ensure remarks are relevant, infrequent and easy to silence.

## References

- [Ollama vision capability and Base64 image API](https://github.com/ollama/ollama/blob/main/docs/capabilities/vision.mdx)
- [OpenCvSharp4 Windows NuGet](https://www.nuget.org/packages/OpenCvSharp4.Windows/)
- [Microsoft System.Speech package](https://www.nuget.org/packages/System.Speech/)
