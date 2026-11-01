# Master Loke tutorial and feature access

[English](TUTORIAL_SYSTEM.md) | [简体中文](TUTORIAL_SYSTEM.zh-CN.md)

## Product behavior

Tasks are available from Lv.1. Focus opens at Lv.2. Boss and Leaderboard open at Lv.5. These rules apply to existing and new saves independently of tutorial completion. Dashboard, Tasks, Settings, Chat and the existing equipment-unlock rule are unchanged. Later bosses retain their previous-victory and Lv.10/Lv.20 requirements. Master still requires a Boss 3 victory.

The sidebar retains each entry with a SimpleCasual lock and hover/keyboard explanation. Dashboard Focus and leaderboard cards display locked content until eligible. Navigation, Focus creation, Boss entry and leaderboard readers enforce the same access rule. Existing live Focus and battle results can still settle, cancel or recover. restarting after an unverifiable interruption keeps the existing interruption semantics.

New players meet Master Loke and create a real task. Saving advances the guide. tutorial completion never marks work complete or grants rewards. The first real reward explains the actual receipt. Focus and Boss/leaderboard introductions become available with the level gates. Unlock notices wait for reward/level-up presentation and never navigate without the user's choice. Daily Reward and equipment-access explanations observe real eligibility and claims.

Teaching uses only Skip and Continue. Skip disables automatic teaching without bypassing locks. Settings > Help provides topic explanations and Tasks start/resume. Locked-topic explanations do not open the underlying feature. Old saves do not automatically replay already-unlocked introductions.

## Ownership and authored UI

- `TaskService.Tutorial.cs`: shared feature access and atomic tutorial-state writes. `PlayerData.tutorial` is optional for old saves, with its own tutorial version. save schema remains 5.
- `TutorialController`: observes committed state and coordinates the guide. it never grants rewards or confirms gameplay actions.
- `StoryDialogueView`: shared unscaled text reveal used by StoryController and the tutorial. Existing Story movement, camera, input and ending are preserved.
- `Assets/Prefabs/Tutorial/MasterTutorial.prefab`: editable guide, Master Loke preview, dialogue, help and highlighter. Canvas contains an instance with serialized page references. No runtime whole-page generation.
- `FeatureLockUI`: authored lock/tooltip/card presentation, including keyboard interaction restrictions on locked dashboard controls.
- `CharacterHomePreview`: optional fixed framing and animation settings for the mentor. existing callers retain their default settings.

`TutorialSpotlight` draws an authored dimming overlay around the current target and blocks other pointer input. Keyboard selection and navigation stay within the permitted target controls and Skip/Continue. the previous selection/navigation are restored on close, disable or focus loss. Explanatory steps (including the task completion checkbox) highlight without enabling the underlying action. Task creation and Focus setting/start steps permit only the relevant controls.

During text reveal, a full-screen transparent input shield owns pointer clicks. Clicking anywhere reveals the complete sentence without forwarding that click to a control below. The task input keeps its keyboard focus during the next sentence so that entering the guide does not cancel its draft. A SimpleCasual Tutorial_Focus_Icon_Up pointer marks actionable targets. Task tabs, + and History, Boss stage selection and Challenge, and Focus Start require the player's actual target click. Pure explanations keep Continue. The task completion checkbox is never forced.

Leaderboard teaching uses three Continue-driven explanations on the existing Account view: display name, upload choice, privacy. The name, upload switch, login and save controls stay read-only for the explanation. Neither account preferences nor authentication is changed. The tutorial closes only the Account view it opened, and pauses the page's competing keyboard navigation while it owns input. The optional leaderboardStep is saved through the existing atomic tutorial path.

Tasks uses nine page explanations: Today, Daily, Todo, Tags, Search, the add button, real input/save, the completion checkbox, then History. The player clicks the highlighted tabs and + button. Continue cannot bypass required target clicks. The save step enables Continue only after a successful real save and stays on that step until clicked. The checkbox is highlighted without allowing completion. Existing taskStep (the committed save receipt) is preserved. taskPageStep separately records explanation progress.

Boss uses seven explanations: selected enemy, cat health/stats, the stage selector, later-boss requirements, mastery, Master Stage, then the Challenge/Ticket confirmation rule. The player clicks the existing stage selector before its middle explanations and closes only its own popup on step changes, Skip, disable, page changes or focus loss. Resuming restores the same bossStep. Challenge must be clicked by the player. Its original confirmation remains authoritative, and the guide never confirms entry or claims mastery rewards. Level and win prerequisites remain authoritative.

Focus is a seven-step guide: Pomodoro purpose, timer modes, focus duration, Rest duration, optional tags, finish/cancel behavior, then Start. Continue advances the descriptive steps. Settings changes never advance it automatically. The final Start step requires an actual click on Start Focus. The step is saved through the existing tutorial-state path. Help restarts this sequence. Finishing an explanation never starts a session.

Equipment uses seven Continue-driven explanations in Cat Home: mastery rewards and the empty Backpack, the three slots, category filters, equip/unequip, upgrade costs and the Cat Level limit, Auto Select, and set bonuses. The navigation explanation waits for Continue before opening Cat Home. These steps do not enable equipment actions or spend currency. equipmentStep persists explanation progress. Settings > Help > Rewards continues into this guide and restarts it. Existing read receipts remain respected. New equipment text uses the existing text reveal fallback until matching voice clips are authored.

The tutorial highlight retains all three SimpleCasual rounded layers, using project-owned copies with nine-slice borders and Sliced Images so corners are not stretched. The dimming overlay and input restrictions are unchanged.

The dialogue copy comes from IntroStory's DialoguePanel, retaining the original font/artwork. Reference components are SimpleCasual Light/Lobby_Tutorial_Message, Light/Popup_Tutorial, Tutorial_Focus_RoundSquare and PictoIcon_Lock_1. Buttons reuse the existing Settings button style and JuicyButton. Master Loke uses Sensei Sprites with sensei-talk while revealing text and sensei-idle afterward.

## Compatibility and backup

No balances, history, equipment or progression values are reset. Before the first tutorial-state write to a valid old save, PlayerDataStore preserves `catquest-data.json.before-tutorial.bak` once, alongside its existing rolling atomic `.bak`. Failed writes leave committed gameplay and tutorial state intact. Null legacy tutorial fields are explicitly preserved through JsonUtility copies.

Tests use isolated saves under Library. Never run tutorial demonstrations by resetting the user's live save.

## Verification

- `Temp/verify-tutorial.request`: bounded tutorial rules + Play Mode flow. results and screenshots in timestamped `Library/TutorialVerification/` directories. Restores prior save environment, start scene, pause and Game View settings.
- `Temp/tutorial-rules.result`: narrow level-gate, save-failure, task receipt, duplicate settlement, legacy continuation/recovery, boss prerequisites, migration/backup/reload checks plus existing TaskSystemVerification.
- Existing aggregate: `Temp/verify-battle-rules.request`, which includes FocusImplementationVerification and RewardSystemVerification. Focus fixtures now enter at Lv.2 and assert the unchanged level-scaled rewards explicitly.

Screenshots alone do not establish animation or interaction quality. Review the current run's images/capture, logs and result files. historical PASS files are not current evidence.
