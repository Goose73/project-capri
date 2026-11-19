"""Project Capri local Kokoro speech backend.

Responds to the same loopback-only /synthesize protocol as the existing
optional neural TTS adapter. No microphone, cloud API, file logging,
persistent transcripts or remote connections. Install model files separately.
Kokoro (model: Apache-2.0; Python wrapper: MIT).
"""
from __future__ import annotations

import io
import json
import math
import threading
import numpy as np
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import soundfile as sf
from kokoro_onnx import Kokoro

BASE = Path(__file__).resolve().parent
MODEL = BASE / "models" / "kokoro-v1.0.int8.onnx"
VOICES = BASE / "models" / "voices-v1.0.bin"
HOST = "127.0.0.1"
PORT = 5000
DEFAULT_VOICE = "af_heart"
# These are synthetic model voices, not samples or clones of show performers.
ALLOWED_VOICES = frozenset(
    {"af_heart", "af_bella", "af_sky", "af_nova",
     "af_sarah", "af_nicole", "bf_emma", "bf_isabella"}
)

# Initialize once; concurrent requests share the model but not its state.
engine = Kokoro(str(MODEL), str(VOICES))
synthesis_lock = threading.Lock()


class CapriSpeechHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "CapriVoice"
    sys_version = ""

    def log_message(self, format: str, *args: object) -> None:
        # Never log user prompts, text, voice transcripts or addresses.
        pass

    def reply(self, status: int, payload: bytes, content_type: str) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(payload)
        self.close_connection = True

    def problem(self, status: int) -> None:
        # Fixed errors only. Do not echo input text.
        self.reply(status, b'{"ok":false}', "application/json")

    def do_GET(self) -> None:
        if self.path != "/health":
            self.problem(404)
            return
        body = json.dumps(
            {"ok": True, "engine": "kokoro-onnx", "voice": DEFAULT_VOICE,
             "voices": sorted(ALLOWED_VOICES)},
            separators=(",", ":"),
        ).encode("utf-8")
        self.reply(200, body, "application/json")

    def do_POST(self) -> None:
        if self.path != "/synthesize":
            self.problem(404)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if size < 2 or size > 4096:
                self.problem(413)
                return
            req = json.loads(self.rfile.read(size))
            if not isinstance(req, dict):
                self.problem(400)
                return
            text = req.get("text")
            voice = req.get("voice", DEFAULT_VOICE)
            length_scale = req.get("length_scale", 1.0)
            volume = req.get("volume", 1.0)
            if not isinstance(text, str) or not 1 <= len(text.strip()) <= 1000:
                self.problem(400)
                return
            if voice not in ALLOWED_VOICES:
                self.problem(400)
                return
            if isinstance(length_scale, bool) or not isinstance(length_scale, (float, int)):
                self.problem(400)
                return
            length_scale = float(length_scale)
            if not math.isfinite(length_scale):
                self.problem(400)
                return
            if isinstance(volume, bool) or not isinstance(volume, (int, float)) or not math.isfinite(volume):
                self.problem(400)
                return
            volume = max(0.0, min(1.0, float(volume)))
            # Expressive, brisk but intelligible speech. Server never uses
            # settings to download arbitrary voice files.
            speed = max(0.80, min(1.20, 1.03 / max(0.8, min(1.25, length_scale))))
            lang = "en-gb" if voice.startswith("bf_") else "en-us"
            with synthesis_lock:
                audio, sample_rate = engine.create(
                    text.strip(), voice=voice, speed=speed, lang=lang
                )
            if audio is None or len(audio) == 0:
                self.problem(503)
                return
            output = io.BytesIO()
            sf.write(output, np.clip(audio * volume, -1.0, 1.0),
                     sample_rate, format="WAV", subtype="PCM_16")
            data = output.getvalue()
            if not 44 <= len(data) <= 8 * 1024 * 1024:
                self.problem(503)
                return
            self.reply(200, data, "audio/wav")
        except (ValueError, TypeError, UnicodeDecodeError, json.JSONDecodeError):
            self.problem(400)
        except Exception:
            # No text, model path or traceback is sent over HTTP.
            self.problem(503)


if __name__ == "__main__":
    with ThreadingHTTPServer((HOST, PORT), CapriSpeechHandler) as server:
        server.serve_forever(poll_interval=0.2)
