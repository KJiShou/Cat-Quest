# Historical audio design prompts

[English](SOUND_PROMPTS.md) | [简体中文](SOUND_PROMPTS.zh-CN.md)

> Historical record: these locally synthesized files were replaced by user-approved third-party assets on 2026-10-31. Current sources, rights and hashes are in `AUDIO_MANIFEST.json` and [SOUND.md](SOUND.md). Do not run the old generator over the approved audio.

These were original local synthesis, without an AI audio model, paid API or third-party samples. The prompts below describe sound design implemented through explicit melodies, synthesis and envelopes, not language-model requests.

The historical files were 48 kHz/16-bit/stereo WAV. Music used 16-bar loops with ambience tails wrapped to the start. Effects had smooth boundaries and headroom below 0 dBFS for simultaneous playback.

## Warm cat garden — `musicClip`

File: `Assets/Audio/Music/menu_garden.wav`. Duration: 48.0 seconds,80 BPM.

> Warm, playful instrumental for a cozy cat productivity game. 80 BPM, C major, 16 bars. Soft rounded kalimba-like plucks, mellow electric-piano chords, warm bass and tiny brushed percussion. A gentle original four-note motif with sparse variations. Light, welcoming, never busy. No vocals, animal voices or abrupt accents. Exact musical loop with ambience wrapping across the seam.

## Quiet focus clouds — `focusMusic`

File: `Assets/Audio/Music/focus_clouds.wav`. Duration: 60.0 seconds,64 BPM.

> Quiet concentration instrumental. 64 BPM, C major/A minor colour, 16 bars. Slow soft sine-based pad chords, sparse rounded electric-piano notes, very gentle bass. No drums, no bright lead, no ticking, no vocals. More space and lower level than menu music. Steady and unobtrusive for long work sessions. Exact musical loop, no dramatic build or ending.

## Sunbeam rest — `restMusic`

File: `Assets/Audio/Music/rest_sunbeam.wav`. Duration: 53.333 seconds,72 BPM.

> Restful, reassuring instrumental for a short break. 72 BPM, C major, 16 bars. Warm sustained chords, occasional soft bell and relaxed plucked arpeggios over gentle bass. Airy stereo space, no urgent rhythm, no vocals or nature recordings. A simple uplifting melody related to the menu theme. Exact musical loop with no fade-to-silence between repetitions.

## Brave paws battle — `battleMusic`

File: `Assets/Audio/Music/battle_paws.wav`. Duration: 32.0 seconds,120 BPM.

> Friendly fantasy cat boss-battle instrumental. 120 BPM, A minor, 16 bars. Rounded synth-pluck ostinato, soft electronic kick, restrained noise snare and hats, warm pulsing bass, short original heroic melody. Energetic but cute, not horror or aggressive metal. Keep room for impact sounds. No vocals, no sampled orchestra, no explosive transition. Exact musical loop.

## Soft button click — `buttonClick`

File: `Assets/Audio/SFX/button_click.wav`. Duration: 0.14 seconds.

> One soft tactile UI pop, 0.14 seconds. Rounded wooden/plastic-like synthesized tap with a tiny descending pitch bend and a dry delicate transient. Immediate response, quiet and pleasant when repeated. No metallic ringing, double click, hiss bed or reverb.

## Settings volume preview — `previewClip`

File: `Assets/Audio/SFX/settings_preview.wav`. Duration: 0.65 seconds.

> One gentle two-note synthetic marimba preview, 0.65 seconds. Mid-register C then E, evenly balanced and clearly audible for setting sound volume. Rounded attack, short clean decay, no loud bass or sharp treble. Distinct from a success fanfare.

## Task completion — `taskComplete`

File: `Assets/Audio/SFX/task_complete.wav`. Duration: 1.05 seconds.

> A warm three-note ascending success chime, 1.05 seconds, C-E-G. Soft bell/pluck blend, subtle sparkle and a compact pleasant tail. Small everyday achievement, not a grand victory. No coin cascade, speech or long reverb.

## Cat level-up — `levelUp`

File: `Assets/Audio/SFX/level_up.wav`. Duration: 1.8 seconds.

> A cheerful original rising level-up jingle, 1.8 seconds, C-E-G-high C, with a soft major chord underneath the final note. Rounded digital bell, slightly brighter than task completion, compact shimmering tail. Cute and rewarding, no piercing arcade siren or vocals.

## Cat sword hit — `catAttack`

File: `Assets/Audio/SFX/cat_attack.wav`. Duration: 0.24 seconds.

> Single small fantasy sword swish and compact impact, 0.24 seconds. Quick filtered-air sweep into a light woody-metallic tick, low-volume low-mid impact body. Agile, playful, readable during repeated attacks. No scream, gore, long ringing blade or background ambience.

## Boss impact — `bossAttack`

File: `Assets/Audio/SFX/boss_attack.wav`. Duration: 0.36 seconds.

> Single stylized boss impact, 0.36 seconds. Short descending low-mid thump with a muted crunchy noise transient and a very small air rush. Heavier than the cat attack but comfortable at desktop volume. No sub-bass explosion, voice, gore or long rumble.

## Battle victory — `battleVictory`

File: `Assets/Audio/SFX/battle_victory.wav`. Duration: 2.6 seconds.

> Original short friendly victory fanfare, 2.6 seconds. Rising C-E-G, a brief pause, then bright high C over a warm C-major chord. Rounded synth bells and gentle bass, celebratory but restrained, clean final decay. No orchestra samples, crowd, vocals or excessive loudness.

## Gentle defeat — `battleDefeat`

File: `Assets/Audio/SFX/battle_defeat.wav`. Duration: 1.8 seconds.

> Kind, non-punishing defeat cue, 1.8 seconds. Three soft descending notes E-C-A with a mellow minor-colour pad and a warm fading low note. Encourages trying again rather than alarming the user. No buzzer, harsh dissonance, voice or dramatic crash.

## Focus completion bell — `focusComplete`

File: `Assets/Audio/SFX/focus_complete.wav`. Duration: 1.6 seconds.

> A clear calm two-note completion bell, 1.6 seconds. C then G, soft mallet attack and restrained airy harmonics, enough sustain to notice without startling. Distinct from task reward and from rest completion. No alarm loop, ticking, speech or harsh high-frequency ping.

## Rest completion — `restComplete`

File: `Assets/Audio/SFX/rest_complete.wav`. Duration: 1.3 seconds.

> A gentle return-to-work cue, 1.3 seconds. Three rounded ascending mallet notes G-A-high C, a little more rhythmic than the focus-complete bell. Friendly invitation, clean soft tail, moderate volume. No alarm, urgency, voice or repeated beeping.

## Historical adjustment and verification

Generator: `Tools/Generate-CatQuestAudio.py` (Python + numpy). It refuses overwrites by default. `--replace` is only for an explicitly approved whole-pack replacement, never routine checking. It does not change metadata, prefabs or player preferences.

Historical checks covered valid PCM, nonempty signal, no peak clipping and loop-seam sample differences, with statistics/SHA-256 in the then-current manifest. The current manifest describes replacement assets. Machine checks do not replace listening for natural loops, dense repeated hits or clear notifications.
