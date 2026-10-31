> 历史记录：下列本地合成素材已于 2026-10-31 被用户确认的第三方素材替换。当前文件、授权与校验值见 AUDIO_MANIFEST.json 和 SOUND.md；不要运行旧生成器覆盖当前音频。

# Cat Quest 声音提示词与素材

制作方式：本地原创合成；没有调用 AI 音频模型、付费 API 或第三方采样。以下英文 prompt 是声音设计说明；实际由显式旋律、音色合成和包络参数实现，不是把自然语言交给模型。

所有素材为 48 kHz / 16-bit / stereo WAV。BGM 为 16 小节循环，混响/延迟尾音回卷到开头；短音效首尾平滑。保留低于 0 dBFS 的余量以容纳同时播放。

## 温暖猫咪花园 — `musicClip`

文件：`Assets/Audio/Music/menu_garden.wav`；时长 48.0 秒；80 BPM。

> Warm, playful instrumental for a cozy cat productivity game. 80 BPM, C major, 16 bars. Soft rounded kalimba-like plucks, mellow electric-piano chords, warm bass and tiny brushed percussion. A gentle original four-note motif with sparse variations. Light, welcoming, never busy. No vocals, animal voices or abrupt accents. Exact musical loop with ambience wrapping across the seam.

## 安静专注云朵 — `focusMusic`

文件：`Assets/Audio/Music/focus_clouds.wav`；时长 60.0 秒；64 BPM。

> Quiet concentration instrumental. 64 BPM, C major/A minor colour, 16 bars. Slow soft sine-based pad chords, sparse rounded electric-piano notes, very gentle bass. No drums, no bright lead, no ticking, no vocals. More space and lower level than menu music; steady and unobtrusive for long work sessions. Exact musical loop, no dramatic build or ending.

## 阳光下休息 — `restMusic`

文件：`Assets/Audio/Music/rest_sunbeam.wav`；时长 53.333 秒；72 BPM。

> Restful, reassuring instrumental for a short break. 72 BPM, C major, 16 bars. Warm sustained chords, occasional soft bell and relaxed plucked arpeggios over gentle bass. Airy stereo space, no urgent rhythm, no vocals or nature recordings. A simple uplifting melody related to the menu theme. Exact musical loop with no fade-to-silence between repetitions.

## 勇敢猫爪战斗 — `battleMusic`

文件：`Assets/Audio/Music/battle_paws.wav`；时长 32.0 秒；120 BPM。

> Friendly fantasy cat boss-battle instrumental. 120 BPM, A minor, 16 bars. Rounded synth-pluck ostinato, soft electronic kick, restrained noise snare and hats, warm pulsing bass, short original heroic melody. Energetic but cute, not horror or aggressive metal. Keep room for impact sounds. No vocals, no sampled orchestra, no explosive transition. Exact musical loop.

## 柔软按钮点击 — `buttonClick`

文件：`Assets/Audio/SFX/button_click.wav`；时长 0.14 秒。

> One soft tactile UI pop, 0.14 seconds. Rounded wooden/plastic-like synthesized tap with a tiny descending pitch bend and a dry delicate transient. Immediate response, quiet and pleasant when repeated. No metallic ringing, double click, hiss bed or reverb.

## 设置音量预览 — `previewClip`

文件：`Assets/Audio/SFX/settings_preview.wav`；时长 0.65 秒。

> One gentle two-note synthetic marimba preview, 0.65 seconds. Mid-register C then E, evenly balanced and clearly audible for setting sound volume. Rounded attack, short clean decay, no loud bass or sharp treble. Distinct from a success fanfare.

## 任务完成 — `taskComplete`

文件：`Assets/Audio/SFX/task_complete.wav`；时长 1.05 秒。

> A warm three-note ascending success chime, 1.05 seconds, C-E-G. Soft bell/pluck blend, subtle sparkle and a compact pleasant tail. Small everyday achievement, not a grand victory. No coin cascade, speech or long reverb.

## 猫咪升级 — `levelUp`

文件：`Assets/Audio/SFX/level_up.wav`；时长 1.8 秒。

> A cheerful original rising level-up jingle, 1.8 seconds, C-E-G-high C, with a soft major chord underneath the final note. Rounded digital bell, slightly brighter than task completion, compact shimmering tail. Cute and rewarding, no piercing arcade siren or vocals.

## 猫咪刀剑命中 — `catAttack`

文件：`Assets/Audio/SFX/cat_attack.wav`；时长 0.24 秒。

> Single small fantasy sword swish and compact impact, 0.24 seconds. Quick filtered-air sweep into a light woody-metallic tick, low-volume low-mid impact body. Agile, playful, readable during repeated attacks. No scream, gore, long ringing blade or background ambience.

## Boss 重击 — `bossAttack`

文件：`Assets/Audio/SFX/boss_attack.wav`；时长 0.36 秒。

> Single stylized boss impact, 0.36 seconds. Short descending low-mid thump with a muted crunchy noise transient and a very small air rush. Heavier than the cat attack but comfortable at desktop volume. No sub-bass explosion, voice, gore or long rumble.

## 战斗胜利 — `battleVictory`

文件：`Assets/Audio/SFX/battle_victory.wav`；时长 2.6 秒。

> Original short friendly victory fanfare, 2.6 seconds. Rising C-E-G, a brief pause, then bright high C over a warm C-major chord. Rounded synth bells and gentle bass, celebratory but restrained, clean final decay. No orchestra samples, crowd, vocals or excessive loudness.

## 温和战败提示 — `battleDefeat`

文件：`Assets/Audio/SFX/battle_defeat.wav`；时长 1.8 秒。

> Kind, non-punishing defeat cue, 1.8 seconds. Three soft descending notes E-C-A with a mellow minor-colour pad and a warm fading low note. Encourages trying again rather than alarming the user. No buzzer, harsh dissonance, voice or dramatic crash.

## 专注完成铃声 — `focusComplete`

文件：`Assets/Audio/SFX/focus_complete.wav`；时长 1.6 秒。

> A clear calm two-note completion bell, 1.6 seconds. C then G, soft mallet attack and restrained airy harmonics, enough sustain to notice without startling. Distinct from task reward and from rest completion. No alarm loop, ticking, speech or harsh high-frequency ping.

## 休息结束提示 — `restComplete`

文件：`Assets/Audio/SFX/rest_complete.wav`；时长 1.3 秒。

> A gentle return-to-work cue, 1.3 seconds. Three rounded ascending mallet notes G-A-high C, a little more rhythmic than the focus-complete bell. Friendly invitation, clean soft tail, moderate volume. No alarm, urgency, voice or repeated beeping.

## 调整与验证

生成器：`Tools/Generate-CatQuestAudio.py`（Python + numpy）。默认拒绝覆盖；确认要替换整套音频后才使用 `--replace`。它不改 .meta、prefab 或玩家设置。

机器检查：有效 PCM、非空信号、峰值无削波、循环接缝采样差；统计和 SHA-256 见 `AUDIO_MANIFEST.json`。这些不代替真实试听。首次试听请检查循环是否自然、战斗连续命中是否太密、提示音是否清楚。
