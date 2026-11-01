# 设置、标签与账号

[English](SETTINGS.md) | [简体中文](SETTINGS.zh-CN.md)

通过主界面顶栏齿轮打开 Settings。使用 Fredoka 和 `Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Settings.prefab` 的图形层级，预览为 `Preview/100_Settings.png`。旁侧提供 Account、Manage tags 和 AI，Google 登录保留官方图形作为品牌例外。

## 控件与本地保存

- **Notification** 控制 Windows 专注与休息结束提醒，立即保存。
- **Sound/Music** 分别控制应用内音源 0–100%，零显示 Muted。拖动预览，释放、键盘取消选择、短暂键盘空闲或关闭时提交。失败提示并恢复已保存音量。
- **Default task tab** 选择进入 Tasks 时显示 Today、Daily Tasks 或 Todo，立即保存，失败恢复已保存选项。旧存档和无效选项默认 Today，具体任务跳转优先于默认页。
- **Dashboard leaderboard** 选择 Boss Stage、Tasks Completed、Focus Minutes、Power，立即保存并控制预览和 View All 目标，旧存档默认 Focus Minutes。
- **Show on Dashboard** 独立控制 Tasks、Daily rewards、Focus、Leaderboard、Character，默认全显示。保存失败恢复，隐藏卡离开布局、空行收起，没有上方卡时快捷行保持紧凑，全隐藏显示设置提示。仅改变 Dashboard 表现，不停止计时或禁用侧栏功能，隐藏排名保留分类。
- 关闭、Escape、背景返回上一级或退出，退出恢复齿轮焦点，Tab/Shift+Tab 限在当前视图，键盘选标签操作会滚动到可见。
- `TaskService.SaveSettings` 使用已有 `catquest-data.json` 原子备份。旧 version 5 增加默认：通知开、音效 0.594 显示 59%、音乐零，保留任务和成长。

## 标签管理

列表显示颜色和当前任务关联数，New tag 创建，Edit 改名色。名字去两端空白、1–30 字符、不分大小写唯一。调色板以既有浅绿起始，使用 SimpleCasual 蓝、绿、海军蓝、橙、红。

任务引用稳定 `QuestTag.id`，不是显示名或颜色。改名色同步当前行、详情、编辑标签、分组、筛选下拉，保留分配和 ID 筛选。目录刷新仅更新草稿标签控件，不丢未保存文本或空子任务行。

删除需显示受影响任务数并确认，解除当前任务和草稿关联，保留任务、历史和奖励。旧编辑不能复活已删标签，历史保留名字颜色快照，缺旧色时用原浅绿。Tasks 的 Tags 页负责分组，管理仍在 Settings。

## 账号与隐私

Account 支持本地校验显示名 1–20 字符、浏览器 Google 登录、退出和可选分享。配置缺失时禁用登录并解释，本地功能可用，关闭 Account/Settings 取消待登录。

登录不发布，开启分享只上传 `playerId`、`playerName`、`bossStage`、`totalTasksCompleted`、`totalFocusMinutes`、`power`、`catLevel`、服务器 `updatedAt`，不含任务标题描述、日程标签、目标责任、AI 或详细历史。主存档仍权威，这不是云备份恢复。[排行榜](LEADERBOARD.zh-CN.md) 登录后读取汇总。

首次参与把存档绑定 Firebase UID，退出保留进度和绑定，错误账号拒绝发布。另一设备发布引起暂停和明确比较替换，不合并统计。关闭分享停止上传并排队移除，离线仍显示等待。网络失败不撤销任务、专注、装备和奖励。

浏览器 OAuth、Firebase REST、凭证、冲突、规则部署见 [Firebase 配置](Firebase/SETUP.zh-CN.md)。服务与配置由使用者提供，实现不需 Unity Firebase SDK。

## Windows 提醒与音频编辑

主 Windows 程序在通知开启时注册提醒图标。自然 Focus 完成只有保存成功才通知，失败等重试成功。Rest 仅自然结束通知。End Early 保留适用结算休息行为但不冒充自然完成，Cancel、无效中断、手动结束 Rest 不提醒。End Early 后自然结束的 Rest 仍可提醒。

通知只含通用 Focus/Rest 文本，点击请求恢复激活主窗。伙伴不拥有或重复通知，每活会话去重，之后开启通知不补发先前禁用失败的通知。Windows 控制实际显示和免打扰，Editor 不显示原生提醒，通知错误不改保存结果。

`Assets/Resources/SettingsAudio.prefab` 有独立 Sound/Music AudioSource。musicClip 为循环音乐，previewClip 为预览，同一管理器还有 Focus/Rest/Battle 和按钮、任务、升级、战斗、计时音效，见[声音指南](SOUND.zh-CN.md)。其他反馈使用 SettingsAudio.PlaySound。未配置片段静音，应用 Sound 与 Windows 通知声分别控制。

静态视图与标签模板位于 `Assets/Prefabs/Settings/Settings.prefab`，Canvas 齿轮的 SettingsLauncher 打开序列化、预放的实例。标签色使用 BasicFrame_Oval_48_White 并保留几何。只改必要属性，验证不得生成修复预制体。

Settings/Account/Tags 使用项目蓝色按钮与文本输入强调、蓝色着色的 Toggle_Switch_WhiteCustom 和 Slider15_WhiteCustom，开关和音量有活动静音色，零值图标一致。齿轮原色、标签语义色、错误色和 Google 品牌保留。

## 验证入口

在已打开的 Editor 一次一个流程，隔离存档、截图报告放 `Library/` 或 `Temp/`。

| 入口 | 内容和输出 |
| --- | --- |
| `Temp/verify-settings-data.request` | Focus → Rewards → Tasks，再查设置持久绑定和账号，`Temp/verify-settings-data.result`、`Library/SettingsVerification/Data-*` |
| `Temp/verify-settings-play.request` | CharacterHome 隔离参考与 Settings/Tags/编辑/Account，两标准尺寸，控件动效、模态焦点、音量提交重载、分类开关、草稿删除空错态，以及四尺寸双侧栏全部 32 种 Dashboard 显隐组合，`Library/SettingsVerification/<timestamp-guid>/result.txt` 与 latest-run.txt |
| Tools > Cat Quest > Verify Settings Account 或 `Temp/verify-settings-account.request` | 模拟账号汇总、OAuth 回调、冲突和移除，`Temp/verify-settings-account.result`、`Library/SettingsAccountVerification/`，无真实凭证网络 |
| `Temp/build-settings-windows.request` | 启用场景的 Windows 开发版，`Builds/SettingsVerification/Build-*/`、`Temp/build-settings-windows.result` 和 `Library/SettingsVerification/Build-*/`，成功构建不等于提醒验证 |

Play 流程恢复原环境、起始场景、Game View 选择和自定义数、时间比例、暂停及场景状态。运行时改脚本会中止失败，编译后重跑，不保存临时场景或重置真实数据。

在新 PowerShell 运行 `Tools/Verify-SettingsNotifications.ps1 -Executable '<absolute path to the development CatQuest.exe>' -Run -AttemptBannerClick`。使用隔离存档，恢复 PlayerPrefs、前台窗和光标。识别真实系统通知、等待命中区域稳定并点击标题，同时要求原生回调和主窗前台恢复。缺失通知或激活则失败，不注入假回调。免打扰或其他通知可能阻挡，需可用桌面且不要同时操作鼠标。证据在 `Temp/SettingsVerification/native-*`。

新截图需与参考比较，动效帧需观察。原生显示、点击恢复、清理和真实 Google/Firebase 联调另测，上述只是入口描述，不是当前通过证明。

## 可选 ChatGPT

Settings → AI 独立于排行榜 Google 账号。Continue with ChatGPT 使用本机注册打开系统浏览器，关闭取消待登录，已建立连接继续可用。Disconnect ChatGPT 清本地 token 并尝试远端撤销，未确认撤销会明确提示。

用户消息和所请求任务事实通过可用 ChatGPT 计划发送至 OpenAI，提议需确认，难度手动。选择账号支持模型，可打开 Manage usage。断连或额度不足仍可手动任务。聊天单独位于 `ai/chat.dpapi`，清空不删任务。

配置为应用数据目录 `ai/preferences.json`，安装身份 `ai/host.json`，连接 `ai/connection.dpapi` 使用 Windows 当前用户 DPAPI，不改玩家 JSON。协议与隔离测试见 [AI 指南](AI_IMPLEMENTATION.zh-CN.md)，不要把凭证写报告或源代码。

## 按钮外观与任务视图偏好

Settings 遵循[紧凑按钮配色](.impeccable.zh-CN.md#紧凑按钮外观)：次操作天蓝、主操作蓝色、删除淡红；排行榜／默认分类选中项为藏蓝底白字。受影响切片控件使用 multiplier 2 与更薄阴影，标签颜色和账号品牌保持例外。

Default task tab 作用于 List。同一次运行中返回已启用的 Calendar 会保留月份和选择；重新启动先进入 List，并使用已保存的默认分类。
