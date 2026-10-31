# Approved Story and Tutorial audio

`engine.cjs` is the unmodified engine downloaded from https://babble.wrongfloor.studio/src/engine.js on 2026-10-31, version 1.4.0 (MIT, see LICENSE.txt). Its SHA-256 is recorded in the generated catalog. It runs offline in Node and is not shipped as executable Unity code.

The user approved preview B (Fern) for the player and D (Lowell) for Master Loke. `render.cjs` pins those exact settings, neutral mood and seed 31. WAVs are the engine's dry exports, without browser playback reverb. No other audio processing or pitch shift is applied.

From the repository root:

```
python Tools/DialogueAudio/generate.py
python Tools/DialogueAudio/generate.py --check
```

Requires Node and PyYAML. Generation reads the scene and tutorial source without editing them. Dialogue steps are resolved through the scene's actual character references; duplicate role/text pairs share a clip. Tutorial sentences are extracted from C# literals, with explicit composed locked/error messages. If tutorial construction changes beyond the existing patterns, update extraction and review catalog coverage. Never run generation as part of verification.

Existing asset metas are retained. Obsolete audio is not deleted automatically: review references before retiring it. New unknown text remains visible with the original typewriter timing and no voice until regenerated. Player names and reward amounts use offline placeholders; no live save is read.
