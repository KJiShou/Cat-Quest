# Cat Quest 音频配置

项目的 Sound Manager 是现有 `SettingsAudio`，由 GameDataManager 自动创建并跨场景保留。不要再放第二个 manager。

## 已配置的本地素材

已制作并绑定 14 个原创本地合成 WAV：4 首循环 BGM 和 10 个音效，分别位于 `Assets/Audio/Music`、`Assets/Audio/SFX`。每个声音的英文 prompt、中文说明和时长见 `SOUND_PROMPTS.md`，参数与文件校验值见 `AUDIO_MANIFEST.json`。这些是程序合成音频，不是付费 AI 模型生成音频。

BGM 使用 Streaming + PCM 导入，保留循环接缝；音效使用 Decompress On Load + PCM。保留已有音量设置，Music 仍可能为 0。

## 日后替换素材的拖放步骤

1. 退出 Play Mode，将音频导入项目（例如新建 `Assets/Audio/Music` 和 `Assets/Audio/SFX`）。
2. 在 Project 中选择 `Assets/Resources/SettingsAudio.prefab`，在 Settings Audio 组件的以下栏位拖入 AudioClip；直接编辑 prefab 资产，不是运行时实例。
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
| Button Click | 短而轻的 click / pop | JuicyButton 的鼠标左键点击、键盘提交 |
| Preview Clip | 短预览音，可复用 click | Settings 音效预览；留空时使用 Button Click |
| Task Complete | 清脆、温和的成功提示 | Dashboard / Tasks 完成成功后 |
| Level Up | 短升级旋律 | 已提交的升级事件 |
| Cat Attack | 短刀剑挥击/命中组合 | 实际 Boss 战斗的猫攻击命中 |
| Boss Attack | 短重击/受伤组合 | 实际 Boss 战斗的 Boss 命中 |
| Battle Victory | 短胜利旋律 | 胜利结果保存成功后 |
| Battle Defeat | 柔和失败提示 | 失败结果保存成功后 |
| Focus Complete | 清晰但不刺耳的铃声 | 正常完成且成功保存的 Focus |
| Rest Complete | 短返回提示 | Rest 自然计时结束 |

最小素材包可先准备：普通 BGM、战斗 BGM、click、成功提示、猫攻击、Boss 攻击、胜利、失败、计时铃声。不同栏位可以重复拖入同一个素材；Focus/Rest BGM 可先留空。

音效一般选短 WAV，BGM 可用 WAV/OGG。长 BGM 可在 Unity Import Settings 选择 Streaming；短音效可用 Decompress On Load。先平衡原始素材响度，避免战斗音效盖过提示。正式发布前确认素材使用许可。

## 行为与边界

- BGM 优先级：战斗 > 当前 Focus/Rest > 普通。切换时停止旧曲、循环新曲；相同曲目不重头播放。当前没有淡入淡出。
- 这是 2D 音频；主程序自动提供缺失的 AudioListener。独立 Companion 不创建音频管理器，避免双重播放。
- Focus 页面训练动画保持安静。跳过 Boss 战斗不会连续播放被跳过的命中。
- End Early 继续沿用原奖励和 Rest 行为，但不冒充正常完成提示；取消/中断没有完成/胜负音效。
- Windows 系统通知声音仍由 Windows 管理，可能与应用内计时提示同时响起。
- Toggle/Slider 沿用原交互；本次没有自动增加 hover、slider 连续声、装备、宝箱或错误音效。
- 音频不授予奖励、不改变伤害、计时或持久化结果。

## 当前素材的试听

确认 Music 音量大于 0；试听普通 → Focus → Rest、进入/离开 Boss、跳过战斗、失败保存后重试、切页、关闭重开。检查鼠标/键盘 click、禁用按钮不响、连续攻击响度、两个音量独立且重开保留。已生成并做文件级检查；实际游戏内试听仍需确认，不能以静态检查替代。
