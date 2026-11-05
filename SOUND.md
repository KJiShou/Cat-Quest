# Cat Quest audio configuration

[English](SOUND.md) | [简体中文](SOUND.zh-CN.md)

## Story and Tutorial voices, 2026-10-31

- The user approved preview B (soft Fern) for the player and D (gentle Lowell) for Master Loke. Both use Babble Engine 1.4.0 Critter Chatter. Tutorial uses D throughout.
- Offline generation from the actual `IntroStory.unity` dialogue steps and current `TutorialController` lines covers 110 deduplicated role/text entries in `Assets/Resources/DialogueAudio/catalog.json`, including all seven equipment explanations and navigation/locked messages. The catalog records original text, synthesis text, settings, duration and SHA-256. Repeated lines share clips; existing WAVs and GUIDs are preserved.
- No player data is uploaded and no runtime networking/synthesis occurs. Offline `{playerName}` becomes apprentice, rewards use placeholder numbers and dynamic errors use a generic phrase. Display text retains actual names, values and errors. These are character babbles, not literal human speech.
- `SettingsAudio` remains the sole manager. A separate dialogue AudioSource follows Sound volume. Story retains ordinary BGM, Tutorial follows the current page/Focus music priority, and Music settings remain unchanged, including zero volume.
- Approved pitch/speed remain fixed. Text reveal slows to the clip duration if speech exceeds ordinary typing time. Revealing a sentence, changing lines, skipping, closing Tutorial, changing page, disabling or losing focus stops the voice. An old caller cannot stop a new caller's voice. Regaining focus does not replay stopped lines.
- Story blank-area/Space advancement uses the existing click. Skip confirmation adds a click only without JuicyButton to avoid duplication. Tutorial retains its JuicyButton click.
- After editing dialogue, explicitly run `python Tools/DialogueAudio/generate.py` to generate audio/catalog only. `python Tools/DialogueAudio/generate.py --check` checks without repairing. Node and PyYAML are required. Existing GUIDs are preserved.
- The upstream engine and MIT notice are in `Tools/DialogueAudio`. Build attribution is `Assets/StreamingAssets/AudioLicenses/babble-engine-LICENSE.txt`. Existing 15 audio files and their license manifest are unaffected.
- `Temp/verify-dialogue-audio.request` writes isolated-save evidence to `Library/DialogueAudioVerification/<run time>/`. Automated checks do not replace listening to the actual mix.

The sound manager is the existing `SettingsAudio`, created by GameDataManager and retained across scenes. Do not add a second manager.

## Configured local assets

On 2026-10-31, 15 WAVs were replaced following final user listening approval: four looping music tracks and eleven effects. The button uses third-preview click-bubble-1, while other files use the retained second-round choices where applicable. Replacements preserved `.meta` GUIDs, `Resources/SettingsAudio.prefab` bindings and playback logic. Sources and hashes are in `AUDIO_MANIFEST.json`, with build notices in `Assets/StreamingAssets/AudioLicenses`.

The approved files are byte-identical 44.1 kHz/stereo/PCM16 WAVs, including preview gain and the Focus reminder's 2.5 kHz low-pass. The web preview player's 80% volume was not baked into assets or player settings. The first three BGM tracks are described by their author as AI-assisted sounds. [SOUND_PROMPTS.md](SOUND_PROMPTS.md) records replaced historical synthesis, not current asset sources.

BGM imports use Streaming + PCM and retain loop seams. Effects use Decompress On Load + PCM. Saved volumes are preserved and Music may still be zero.

## Replacing audio later

1. Stop Play Mode and import clips, for example under `Assets/Audio/Music` and `Assets/Audio/SFX`.
2. Select the asset `Assets/Resources/SettingsAudio.prefab` and assign AudioClips to the Settings Audio fields below, not a runtime instance.
3. Retain existing Sound Source and Music Source bindings.
4. Run and adjust Sound/Music in Settings. **Music defaults to zero and must be raised to hear BGM.** Saved local preferences apply.
5. Unassigned clips remain silent without an error or substitute sound. No page regeneration is required.

## Connected fields

| Inspector field | Suggested asset | Trigger |
| --- | --- | --- |
| Music Clip | Relaxed seamless instrumental | Ordinary state |
| Focus Music | Quiet ambient/lo-fi loop | Focus and save retry |
| Rest Music | Relaxed loop | Rest |
| Battle Music | Playful battle loop | Successfully confirmed Boss entry |
| Button Click | Short light click/pop | JuicyButton left pointer down or keyboard submit. Release does not replay sound and valid release still owns the action |
| Preview Clip | Short preview, possibly a click | Sound preview, falling back to Button Click if empty |
| Task Complete | Gentle clear success cue | Committed Dashboard/Tasks completion |
| Level Up | Short rising phrase | Committed level-up |
| Equipment Claim | Rare-item cue | Successful equipment claim/upgrade feedback |
| Cat Attack | Short sword/impact | Actual cat hit in Boss combat |
| Boss Attack | Short heavy hit | Actual Boss hit |
| Battle Victory | Short victory phrase | Successfully saved victory |
| Battle Defeat | Gentle failure cue | Successfully saved defeat |
| Focus Complete | Clear non-startling bell | Natural Focus completion after successful save |
| Rest Complete | Short return cue | Natural Rest completion |

A minimal pack can share clips across fields: ordinary/battle BGM, click, success, cat/Boss hits, victory/defeat and timer bell. Focus/Rest tracks may remain empty. Short WAV effects and WAV/OGG music are suitable. Streaming is useful for long music, Decompress On Load for short effects. Balance source loudness and confirm usage rights before release.

## Behavior and boundaries

- BGM priority is battle, then active Focus/Rest, then ordinary. Switching stops the old clip and loops the new one. An unchanged clip does not restart. No crossfade is implemented.
- Audio is 2D and the main app supplies a missing AudioListener. The separate Companion creates no manager to avoid duplicate sound.
- Focus training is silent. Skipping combat does not emit every skipped hit.
- End Early retains settlement and applicable Rest behavior but does not impersonate natural completion. Cancel/interruption emits no completion or victory/defeat sound.
- Windows notification sounds remain controlled by Windows and may coincide with in-app timer cues.
- Toggles/sliders retain interaction. No extra hover or continuous slider sound is introduced. Claims and upgrades share Equipment Claim.
- Audio cannot award rewards or change damage, timing or persistence.

## Listening review

Set Music above zero and inspect ordinary → Focus → Rest, Boss entry/exit, Skip, failed-save retry, navigation and reopening. Check mouse/keyboard clicks, silent disabled buttons, repeated combat loudness, independent volume controls and persistence. Browser previews were approved, but in-game mixing, seams and repeated input still require Play Mode listening. File checks are not listening evidence.

## Final approved mapping

| Field | Approved asset | Preview version |
| --- | --- | --- |
| musicClip | Porch Light · Full Loop | 1 |
| focusMusic | Rain on the Window · Lite Loop | 1 |
| restMusic | Lantern Walk · Lite Loop | 1 |
| battleMusic | Battle Theme I · loop | 1 |
| buttonClick | click-bubble-1 | 3 |
| previewClip | pluck_002 | 1 |
| taskComplete | quest-complete-bright | 2 |
| levelUp | FANFARE 11 | 2 |
| equipmentClaim | rare-item-bright | 2 |
| catAttack | knifeSlice | 1 |
| bossAttack | impactPunch_heavy_000 | 2 |
| battleVictory | FANFARE 5 | 2 |
| battleDefeat | game-over-warm | 2 |
| focusComplete | reminder | 2 |
| restComplete | confirmation_002 | 1 |
