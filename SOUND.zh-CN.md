# Cat Quest 音频配置

[English](SOUND.md) | [简体中文](SOUND.zh-CN.md)

## Story / Tutorial 角色声音（2026-10-31）

- 用户确认 B（柔和 Fern）用于主角，D（温柔 Lowell）用于 Master Loke。两者均为 Babble Engine 1.4.0 的 Critter Chatter。Tutorial 全部使用 D。
- 根据 `IntroStory.unity` 的实际 Dialogue steps 和 `TutorialController` 当前台词离线生成音频，`Assets/Resources/DialogueAudio/catalog.json` 当前覆盖 128 个去重的角色/台词条目，包括十八段任务右键菜单说明、七段装备说明及导航、锁定提示。目录记录原台词、生成文本、参数、时长及 SHA-256。同一台词复用文件，保留已有 WAV 和 GUID。
- 不上传玩家数据、不在游戏运行时联网或合成。`{playerName}` 离线使用 apprentice，动态奖励数字使用占位数字，动态错误使用通用错误短语。显示文字始终保留实际姓名、数值和错误。咿呀声不是逐字真人朗读。
- `SettingsAudio` 仍是唯一音频管理器。新增独立讲话 AudioSource，使用 Sound 音量。Story 沿用普通 BGM，Tutorial 沿用当前页面/Focus 的 BGM 优先级。Music 的原有设置不变，包括原本为 0 的情况。
- 保留批准的音高和速度。当语音比原打字时间长时，文字显示放慢至该段语音时长。点击补全文字、换句、跳过、关闭教学、切页、禁用或失去应用焦点会停止当前声音。旧调用者不能停止新调用者的声音。焦点恢复不重播已停止的句子。
- Story 空白区域/Space 推进播放现有 click。Skip 确认控件在没有 JuicyButton 时补上 click，避免重复。Tutorial 原有 JuicyButton click 保留。
- 新增/修改台词后显式运行 `python Tools/DialogueAudio/generate.py`。只生成音频和清单，不生成 UI。`python Tools/DialogueAudio/generate.py --check` 仅检查，不修复资产。需要 Node 和 PyYAML。生成保留已有 GUID。
- 上游引擎原文件和 MIT 许可保存在 `Tools/DialogueAudio`。构建署名位于 `Assets/StreamingAssets/AudioLicenses/babble-engine-LICENSE.txt`。不改变既有 15 个音频及其许可清单。
- Unity 专项检查入口：`Temp/verify-dialogue-audio.request`，结果写入 `Library/DialogueAudioVerification/<本次时间>/`，使用独立存档。音频检查不能替代实际设备上的混音听感确认。

项目的 Sound Manager 是现有 `SettingsAudio`，由 GameDataManager 自动创建并跨场景保留。不要再放第二个 manager。

## 已配置的本地素材

已于 2026-10-31 按用户最终试听确认替换 15 个 WAV：4 首循环 BGM 和 11 个音效。按钮使用第三版 click-bubble-1，其余使用第二轮反馈中确认保留的版本。直接替换现有文件内容，保留 .meta GUID、Resources/SettingsAudio.prefab 绑定和播放逻辑。文件与来源见 AUDIO_MANIFEST.json。随构建分发的署名及许可证位于 Assets/StreamingAssets/AudioLicenses。

素材采用已试听的 44.1 kHz / stereo / PCM16 WAV（逐字节一致），包括试听增益及 Focus 提醒的 2.5 kHz 低通处理。网页播放器的 80% 音量仅为试听播放器设置，未写入素材或玩家设置。前三首 BGM 作者声明 AI-assisted sounds。SOUND_PROMPTS.md 仅记录已被替换的历史合成素材，不是当前素材来源。

BGM 使用 Streaming + PCM 导入，保留循环接缝。音效使用 Decompress On Load + PCM。保留已有音量设置，Music 仍可能为 0。

## 日后替换素材的拖放步骤

1. 退出 Play Mode，将音频导入项目（例如新建 `Assets/Audio/Music` 和 `Assets/Audio/SFX`）。
2. 在 Project 中选择 `Assets/Resources/SettingsAudio.prefab`，在 Settings Audio 组件的以下栏位拖入 AudioClip。直接编辑 prefab 资产，不是运行时实例。
3. Sound Source / Music Source 已绑定，保留原绑定。
4. 运行后在 Settings 调整 Sound、Music。**现有 Music 默认值为 0，需要手动调高才能听到 BGM。** 音量沿用现有本地设置。
5. 没有素材的栏位可以留空，不报错、不播放替代音效。无需重新生成页面。

## 准备清单（全部已接入）

| Inspector 栏位 | 建议素材 | 播放时机 |
| --- | --- | --- |
| Music Clip | 轻松、无歌词的菜单 BGM，可无缝循环 | 普通状态 |
| Focus Music | 安静、低干扰 ambient / lo-fi，可循环 | 专注和保存重试期间 |
| Rest Music | 放松 BGM，可循环 | Rest 期间 |
| Battle Music | 活泼战斗 BGM，可循环 | 确认并成功进入 Boss 战斗后 |
| Button Click | 短而轻的 click / pop | JuicyButton 的鼠标左键按下立即播放、键盘提交。松开不重复播放，按钮操作仍在有效松开时执行 |
| Preview Clip | 短预览音，可复用 click | Settings 音效预览。留空时使用 Button Click |
| Task Complete | 清脆、温和的成功提示 | Dashboard / Tasks 完成成功后 |
| Level Up | 短升级旋律 | 已提交的升级事件 |
| Equipment Claim | 稀有装备获得旋律 | 装备领取 / 升级成功反馈 |
| Cat Attack | 短刀剑挥击/命中组合 | 实际 Boss 战斗的猫攻击命中 |
| Boss Attack | 短重击/受伤组合 | 实际 Boss 战斗的 Boss 命中 |
| Battle Victory | 短胜利旋律 | 胜利结果保存成功后 |
| Battle Defeat | 柔和失败提示 | 失败结果保存成功后 |
| Focus Complete | 清晰但不刺耳的铃声 | 正常完成且成功保存的 Focus |
| Rest Complete | 短返回提示 | Rest 自然计时结束 |

最小素材包可先准备：普通 BGM、战斗 BGM、click、成功提示、猫攻击、Boss 攻击、胜利、失败、计时铃声。不同栏位可以重复拖入同一个素材。Focus/Rest BGM 可先留空。

音效一般选短 WAV，BGM 可用 WAV/OGG。长 BGM 可在 Unity Import Settings 选择 Streaming。短音效可用 Decompress On Load。先平衡原始素材响度，避免战斗音效盖过提示。正式发布前确认素材使用许可。

## 行为与边界

- BGM 优先级：战斗 > 当前 Focus/Rest > 普通。切换时停止旧曲、循环新曲。相同曲目不重头播放。当前没有淡入淡出。
- 这是 2D 音频。主程序自动提供缺失的 AudioListener。独立 Companion 不创建音频管理器，避免双重播放。
- Focus 页面训练动画保持安静。跳过 Boss 战斗不会连续播放被跳过的命中。
- End Early 继续沿用原奖励和 Rest 行为，但不冒充正常完成提示。取消/中断没有完成/胜负音效。
- Windows 系统通知声音仍由 Windows 管理，可能与应用内计时提示同时响起。
- Toggle/Slider 沿用原交互。本次没有新增 hover 或 slider 连续声。装备领取与升级继续使用既有 Equipment Claim 槽位。
- 音频不授予奖励、不改变伤害、计时或持久化结果。

## 当前素材的试听

确认 Music 音量大于 0。试听普通 → Focus → Rest、进入/离开 Boss、跳过战斗、失败保存后重试、切页、关闭重开。检查鼠标/键盘 click、禁用按钮不响、连续攻击响度、两个音量独立且重开保留。浏览器试听已由用户确认。实际游戏内混音、循环接缝听感与连续输入仍需 Play Mode 确认，文件级检查不代替实际听感。

## 最终素材对应

| 槽位 | 已确认素材 | 试听版本 |
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
