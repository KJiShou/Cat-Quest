# AI Chat and task proposals

## Product behavior

The Chat navigation page replaces automatic difficulty advice. The existing AI
settings retain browser sign-in, disconnect, model selection and usage management.
Old `automatic` preferences are ignored. There are no background task-analysis
requests. New proposals default to Medium; editing preserves the existing
manual difficulty, and only the user can change it in the review form.

Chat supports conversation, task lookups, new task proposals and edits to existing
active tasks. AI cannot complete/delete tasks or grant rewards. Each proposal is
reviewed in the reused task editor and committed through TaskService. Today uses
the same query as Tasks, including eligible daily and overdue scheduled tasks.
Date/name/ID queries are restricted to the requested filter. A lookup returns at
most 40 tasks and indicates truncation. There are at most three lookups per turn.
Task fields are data, never instructions or executable writes.

The response contract is JSON with `message`, optional `query` and `drafts`.
Queries support today/date/search/id. An edit must reference a task read during
the current turn, preserving unchanged fields and subtask identities. Unsupported
fields, including difficulty and rewards, are rejected. Existing tags and manual
difficulty can be changed in the local review form.

Confirmation uses the existing atomic task save with a guard against changed,
deleted, settled and cross-day targets. New drafts have stable IDs persisted
before task creation. Duplicate saves cannot create another task. Failed storage
writes leave local task data intact. Completion and reward rules are unchanged.

## Conversation and privacy

Only user-sent messages and requested task facts are sent using the connected
ChatGPT plan. Credentials remain protected by Windows DPAPI. The single chat is
stored separately in `ai/chat.dpapi`, also protected by DPAPI; it never changes
the player-save schema. Clearing chat keeps saved tasks.

Requests include up to 20 recent messages / 16,000 text characters. Each message
is limited to 2,000 characters. Older conversation is still visible locally but
may not be sent. Fresh task lookups resolve current facts. One request runs at a
time; stop, disconnect, model changes and clear discard late responses. Changing
pages retains the session. Failed requests can be retried.

The main Unity process owns queries and confirmation. The Companion does not
initialize AI. Tests use injected stores or isolated directories and never use the
live player save. Ordinary JSON remains bounded to four million characters;
transport retains its separate SSE/output limits and 20-second request deadline.

## UI and implementation

Source reference: SimpleCasual `Preview/042_Chat.png`,
`Prefabs_DemoScene_Panels/Light/Caht.prefab` (vendor spelling), and
`Prefabs_Component_Frames/ChatFrame01,02_Light-Group.prefab`.
Project-owned Chat, ChatMessage and ChatTaskDraft prefabs reuse existing task
editing, SidebarNavigationButton and JuicyButton components. Existing Fredoka
fonts are retained, with a runtime Windows Microsoft YaHei fallback for
Chinese in Chat and saved tasks; vendor assets and primary typography remain unchanged.

The new navigation enum is appended after Leaderboard, preserving old indices.
The Canvas change adds the new page/button and removes only advice subtrees.
The Settings change removes automatic advice controls and updates privacy copy.

## Verification entrypoints

- `Temp/verify-ai-data.request`: chat/domain, protocol, transport, bindings, and
  existing Rewards → Tasks/CharacterHome plus account/settings checks. Results:
  `Temp/verify-ai-data.result`, with evidence under `Library/AiVerification/`.
- `Temp/verify-ai-play.request`: isolated mock chat, task review/confirmation,
  cancellation and feedback, with captures at four resolutions and sidebar states.
  Result: `Temp/verify-ai-play.result`.
- `Temp/build-settings-windows.request`: existing Development Windows build flow.
- `--ai-smoke --smoke-dir "<absolute isolated directory>"`: requires the existing
  `ai-smoke.marker`, redirects save and AI files, waits for user browser sign-in,
  verifies a synthetic chat response, then disconnects. This does not authorize
  the browser on the user's behalf.
- `pwsh -NoProfile -File Tools/Verify-ChatGptOAuth.ps1 -SelfTest`: isolated offline
  OAuth/protocol checks. `-Run` performs a separate live authorization experiment.

Old screenshots and reports are historical only. Current checks and any remaining
manual verification gaps are recorded in the implementation delivery report.

## Historical difficulty-advice verification (superseded)

The earlier difficulty-advice implementation was tested on 2026-10-24; these results do not validate Chat.

- Current data/protocol/transport and existing task/reward/settings/Google-account
  regressions passed in
  `Library/AiVerification/Data-20261024-042727-29952f5aa36d4beda33e0d47637f5140/result.txt`.
- Rendered/interactive task and settings checks passed at 1920×1080 and 1280×720 in
  `Library/AiVerification/Play-20261024-041302/`. Screenshots and hover motion frames
  were inspected against the SimpleCasual settings reference and original UI.
- Prefab object-identity comparison: `Temp/ai-serialized-review.json`. No original
  objects were removed. The one-time authoring tool was removed after repeat-run
  idempotence and bindings checks.
- Development Windows build succeeded with zero errors:
  `Builds/SettingsVerification/Build-20261024-042805/CatQuest.exe`.
- Real Windows player verification passed in
  `Temp/AiWindowsSmoke/20261024-122146/result.json`: the user's browser authorization
  established the independent Cat Quest connection; the corrected build restored
  DPAPI credentials after restart, loaded the account's models, forced a serialized
  token refresh, completed one synthetic inference and revoked/disconnected the
  session. `ai-player.png` captures the final state. Only encrypted registration
  remains; test tokens were cleared. `live-save-after.json` confirms the live
  player-save SHA-256 is unchanged.

The real account's catalog exceeded the original 256 KiB HTTP and JSON limits.
The separate limits were corrected together, covered by the transport regression
and rebuilt. Earlier failed runs are retained as diagnostic history, not passes.

Real provider-side denial, exhausted quota, outages and two-minute browser timeout
were not all exercised with this account; protocol/transport negative checks use
isolated synthetic fixtures. Google account regression used the existing verifier,
not a fresh live Google/Firebase login. This is a Windows local prototype only.


## Official protocol references

- [Sign-in and public-client registration](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)
- [Models and inference](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
- [Preview limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations)
- [Accounts and sessions](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions)

## Current Chat verification, 2026-10-24

Data/protocol/task/account/settings regressions passed in
`Library/AiVerification/Data-20261024-063908/result.txt`. Play Mode checks and
reviewed screenshots are in `Library/AiVerification/Play-20261024-063950/`.
The Development Windows build `Builds/SettingsVerification/Build-20261024-064040/`
completed with zero errors. An isolated Windows batch-mode mock chat and restart
passed; this does not establish live ChatGPT inference or native interaction.
Use `--ai-smoke --ai-chat-offline --smoke-dir <isolated-directory>` with the marker
for this mock mode; normal `--ai-smoke` still requires user browser authorization.
Full evidence, limitations and source hashes are recorded under
`Library/AiVerification/Delivery-20261024-0644/`.


## Sensei chat presentation

Chat uses the existing Sensei idle texture in editable circular portrait masks,
with a face crop in the header and assistant message template. The model persona
is Sensei, a patient mentor; query and task-confirmation permissions are unchanged.
The three project Chat prefabs retain their existing objects and bindings. A
padded surface leaves the application background visible around the page.
SimpleCasual `042_Chat`, `ChatFrame01,02_Light-Group`, and `Caht/CharacterFrame`
provide the reference; task-style PictoIcons and the Tasks Today icon accompany
controls and draft proposals. Existing Fredoka and JuicyButton feedback remain.

Presentation verification and limitations are recorded in
`Library/AiVerification/Sensei-20261024/report.md`. The earlier Windows build
predates this visual refinement; this UI-only revision was checked in the open
Unity Editor, not rebuilt for Windows.

## Model dropdown and live Windows follow-up

The AI model selector now has a serialized Toggle item and TMP label binding in
its existing template. Play verification explicitly opens and closes the list at
1920x1080 and 1280x720. Live Windows connection restoration, token refresh,
inference parsing and explicit successful disconnect passed on 2026-10-24.
See `Library/AiVerification/Dropdown-20261024/report.md` for current evidence and
limits, including the earlier invalid aggregate result and its corrected check.

## Chat presentation, language and existing tags

Messages show the existing player portrait on the right and Sensei on the left,
with separate larger speaker labels and plain-text message bodies. The header
uses only Sensei. Disabled Chat controls use neutral grey; the pending label
animates individual glyph vertices with unscaled time and resets on stop/disable.
Idle proposal/context reminders and the review instruction line are removed;
errors and committed save outcomes remain visible.

Draft cards reuse Tasks difficulty badge sprites, schedule/subtask/tag icons and
coloured tag chips. Review uses the existing editor in a bounded, centred panel
with a full Canvas dimmer and fixed footer; only its form content scrolls.

AI Settings > Reply language supports Auto (latest user message), English and
Simplified Chinese. Preferences remain separate from player data; existing
connections default to Auto without resetting login. Explicit language overrides
prior conversation language in the request instructions. Language/model changes
cancel pending chat. Existing task titles stay intact when referred to.

The bounded `tags` lookup exposes only existing tag IDs/names. Model drafts may
select existing tagIds; unknown IDs are rejected. Omitted tagIds preserve an
edited task's tags. AI never creates tags, chooses difficulty or grants rewards.

Current evidence: Library/AiVerification/ChatPolish-20261024/report.md.

### Per-turn language drift guard

Auto resolves English/Chinese from the latest user message once per turn; explicit
settings take precedence. Task/tag lookups and retries within the turn reuse that
language instead of redetecting it from returned task data or old replies.
Other scripts remain model-detected in Auto. This small script heuristic is not a
general language detector (for example, it cannot distinguish English/French).

Before publishing a final reply, obvious Cyrillic drift in English/Chinese or Han
drift in English triggers one regeneration using the same facts. Exact user text
and retrieved task text are exempt so task names can retain their original script.
A second mismatch or additional lookup during correction fails with Retry; rejected
messages and drafts are never saved. Cancellation discards late corrections.
Existing history is retained, including old wrong-language replies.
