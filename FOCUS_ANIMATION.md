# Focus training animation

[English](FOCUS_ANIMATION.md) | [简体中文](FOCUS_ANIMATION.zh-CN.md)

Shared prefab: `Assets/Prefabs/Focus/FocusTrainingBattle.prefab`. The Focus scene, CharacterHome Focus page and Pet Widget use it. One Animator lives on the root and the two actors use SpriteRenderer children. Acorn no longer needs a Spine component here.

## Editing timing in Unity

1. Open the prefab and select `FocusTrainingBattle`.
2. Open **Window > Animation > Animation** (Ctrl+6), choose `FocusTrainingLoop`, enable Preview and scrub.
3. At 60 FPS, Ninja swings on frames 12–41 (0.2–0.7 seconds). Acorn Walk runs on frames 27–50 (0.45–0.85 seconds), returning to Idle on frame 51. Move the sprite keys to adjust timing. Walk is one complete 24-frame cycle in a 132-frame/2.2-second loop.
4. `FocusIdle.anim` is a four-second loop, retaining Ninja's one-second and Acorn's 1.333-second cycles.

`Assets/Animations/Focus/` contains `FocusTrainingLoop.anim`, `FocusIdle.anim`, `FocusTraining.controller` and `AcornFrames/`. Acorn frames are 512×512 with transparency, consistent custom pivot/pixels-per-unit and baked facing toward Ninja. Do not add a child Ninja Animator or Animation Events.

## Focus connection

`FocusTrainingBattle` only writes injected main/Companion state to Animator bool `IsTraining`. Transitions have no Exit Time and zero Duration. Focus trains, while Rest, cancel, finish and disconnect idle. It does not calculate hit timing/counts, manually sample animation, launch actors or restore their positions. `previewWithoutFocusSession` supports direct Play previews in the standalone Focus scene.

## Rebaking

**Tools > Cat Quest > Animations > Bake Acorn and Generate Focus Clips** bakes Idle/Walk from Layer Lab's original Acorn SkeletonData, rebuilds clips/controller and updates the shared prefab without changing vendor originals. This is an explicit authoring operation that resets generated timing. Back up customized clips before rebaking. Verification must not run it automatically.

## Running and verification

The Windows executable is `Builds/Windows/CatQuest.exe`. Distribute its entire build folder.

Evidence belongs in `Library/FocusAnimationVerification/`. `clips.txt` covers ten cycles/1320 frames, one Walk per cycle, fixed positions, seams, clip duration and one root Animator. Native checks separately record actual Animator state, sprite changes, Focus/Rest/cancel and Companion lifecycle. Existing reports are historical unless associated with a fresh run.
