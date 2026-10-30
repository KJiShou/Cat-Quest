"""Author only the equipment reward cue; never replace existing audio or metadata."""
import runpy
import wave
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
synth = runpy.run_path(str(root / 'Tools/Generate-CatQuestAudio.py'))
sr, tone, add = synth['SR'], synth['tone'], synth['add']
path = root / 'Assets/Audio/SFX/equipment_claim.wav'
if path.exists():
    raise SystemExit('Equipment cue already exists; refusing to overwrite.')
audio = np.zeros((sr, 2))
# Immediate rounded pickup, rising into the reveal's 0.18-second size peak.
add(audio, tone(72, .30, 'pluck'), 0, .40, -.1)
add(audio, tone(79, .32, 'bell'), .08, .32, .1)
for note, gain in [(84, .65), (76, .22), (79, .25)]:
    add(audio, tone(note, .80, 'bell'), .18, gain)
add(audio, tone(91, .45, 'bell'), .26, .10, .2)
audio -= audio.mean(axis=0)
audio[:120] *= np.linspace(0, 1, 120)[:, None]
audio[-2400:] *= np.linspace(1, 0, 2400)[:, None]
audio *= .38 / np.max(np.abs(audio))
assert np.isfinite(audio).all() and np.max(np.abs(audio)) < 1
with wave.open(str(path), 'wb') as output:
    output.setnchannels(2)
    output.setsampwidth(2)
    output.setframerate(sr)
    output.writeframes(np.round(audio * 32767).astype('<i2').tobytes())
print(f'{path}: 1.0 seconds, stereo PCM16, peak -8.4 dBFS; original synthesis.')
