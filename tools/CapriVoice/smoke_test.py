from pathlib import Path
import numpy as np
import soundfile as sf
from kokoro_onnx import Kokoro

base = Path(__file__).resolve().parent
models = base / "models"
engine = Kokoro(str(models / "kokoro-v1.0.int8.onnx"), str(models / "voices-v1.0.bin"))
out = base / "previews"
out.mkdir(exist_ok=True)
for voice, lang in (("af_heart", "en-us"), ("af_bella", "en-us"), ("bf_emma", "en-gb")):
    audio, rate = engine.create(
        "Hey, there you are! I've got this. So, what's the plan?",
        voice=voice, speed=1.05, lang=lang,
    )
    path = out / f"capri-{voice}.wav"
    sf.write(path, audio, rate, subtype="PCM_16")
    duration = len(audio) / rate
    peak = float(np.max(np.abs(audio)))
    print(f"VOICE={voice} duration={duration:.2f}s peak={peak:.3f} file={path}", flush=True)
    if not (0.5 < duration < 20 and peak > 0.02):
        raise RuntimeError("Voice smoke test failed (silent or implausible duration)")
