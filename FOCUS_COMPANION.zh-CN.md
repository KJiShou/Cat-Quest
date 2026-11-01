# 专注页面与 Windows 桌面伙伴

[English](FOCUS_COMPANION.md) | [简体中文](FOCUS_COMPANION.zh-CN.md)

在 `Assets/Scenes/CharacterHome.unity` 选择 Focus。支持默认 Pomodoro、Stopwatch 和 Countdown。正计时直到 End Focus，可超过三小时。倒计时接受 1–180 整分钟，默认 25，结束自动结算。只有番茄钟进入休息，其专注和休息滑条各 1–60 分钟，默认 25/5。可从任务标签目录选择一个标签，标签不影响奖励或完成任务。

当前番茄钟专注中只提供带确认的 Cancel，取消不发奖、不进入休息。正常结束结算后进入休息。正计时 End Focus 和倒计时 End Focus Early 保留既有有效时间奖励。历史和底层 End Early 收据继续兼容，不等于取消。

训练场景填满圆角裁切背景，隐藏独立页标题和 Return to Dashboard，使用共用侧栏。各模式按需显示白色专注与休息卡，天空显示计时，湖景内有 Start Focus。标签框按名字不区分大小写搜索，选择替换旧标签并关闭，No tags 清空，点击外部或 Escape 关闭。奖励格复用 Task Details 的蓝色鱼币、EXP 格并补同风格券格，显示预览和已结算值。额度卡显示当日使用量及限制。

## 伙伴窗口

Pet Widget 打开 420×300 默认置顶窗口，浅色标题栏居中显示主程序发布的玩家名，包含 Pin、Minimize、Close。Close 来自 SimpleCasual Lobby_Popup_User_Info，置顶和最小化用 Lucide 图标，许可在 `Assets/Generated/Icons/WindowControls`。伙伴不读玩家存档。

Pin 切换置顶，缩放不改变该状态。伙伴打开时即显示任务栏入口，最小化及恢复后继续保留；最小化不结束专注。UI 遮罩和原生窗口区域均为 16 像素圆角，随缩放刷新。标题栏下是完整训练画面，天空计时、水面会话操作，不显示任务、奖励、预设细节或悬停提示。

所有尺寸下，指针在外时隐藏会话按钮；已获得焦点的伙伴正在使用键盘导航时除外，进入约 0.32 秒淡入、离开约 0.24 秒淡出。Start、Cancel、End Focus、End Focus Early、Finish Rest、Retry Save 共用行为，不改变动画构图。标题栏拖动，四边四角用标准 Windows 光标缩放，最小 240×180。关闭立即隐藏原生窗，再在 Unity 清理时退出子进程，主应用继续计时。

## 键盘与重复输入

Tab 从 Pin 开始键盘导航；Tab / Shift+Tab 在 Pin、Minimize、Close 和当前可用的会话操作之间循环，跳过未显示或已禁用的操作。方向键使用现有 Unity 控件导航，Enter 激活选中按钮。键盘导航时，即使指针在外，会话操作仍保持可见；Escape 清除普通选择并恢复由指针控制的淡入淡出。Cancel 确认框打开时独立管理 Tab／Enter／Escape 焦点；Escape 关闭确认框，不取消专注。

指针离开原生窗口时会明确清除悬停／按压反馈，同时保留键盘选择。应用失焦或最小化会清除键盘选择并复位反馈。如果伙伴已是 Windows 前台窗口，却没有原生键盘焦点，会补回缺失的焦点；不会主动抢占其他应用。这避免 Enter 被作为系统键／全屏切换命令处理。连续提交会话操作仍由现有 pending 请求和主程序防重机制处理，反馈不额外提交命令，也不结算奖励。

## 编辑与构建

伙伴的七个按钮（Start、End/Cancel、Finish Rest、Retry Save、Pin、Minimize、Close）复用项目的 `JuicyButton` / `JuicyButtonMesh`，提供悬停、按压回弹及键盘选中/提交反馈，保持点击区域与原有布局不动。伙伴打开时即显示任务栏入口，最小化及恢复后继续保留。修改伙伴 prefab 后，需重新构建 `Builds/Windows/CatQuest.exe`，Editor 启动的预览才会更新。

时长控件来自 SimpleCasual Light/Material_Level_Up 的 Quantity 行，保留蓝色填充、三角标记、青色加减。点击拖动选整分钟，加减每次一分钟，边界禁用。模式选择来自 `Btn_MenuButton_Rectangle01-Group.prefab` 的 `Btn_MenuButton_Rectangle01_Light`，保留互斥 Toggle、反馈、字体及蓝色选中线，使用循环箭头、秒表和沙漏。正计时隐藏时长卡，倒计时隐藏休息，开始后锁定设置。

Window 根对象保留现有 `CompanionRoundedMask` / `Mask` 所需的 `CanvasRenderer`。未指定子 visual 时，反馈通过网格动画实现，不移动按钮 RectTransform，也不接管点击操作。原生任务栏样式在窗口配置时设置，不再于最小化／恢复时来回切换。

- 直接编辑序列化控件，不运行旧整页或伙伴生成器。
- Windows x64 启用场景顺序为 CatQuestBootstrap、IntroStory、CharacterHome，伙伴以独立参数启动同一 exe，分发完整构建目录。
- Editor 伙伴预览查找 `Builds/Windows/CatQuest.exe`，Editor 保持计时权威。

## 状态与持久化

`FocusController` 是唯一实时计时所有者。`FocusSessionOptions` 保存模式、时长和标签，兼容旧任务上下文。模式零为番茄钟。新收据区分实际 `elapsedSeconds` 和可奖励时间，非番茄钟休息为零，正计时目标时长为零。旧用量、奖励、任务标题和执行快照不重算，缺失时长规范化为 25/5。标签 ID 和名字随收据保存。

`CompanionHost` 在每个主进程独立临时目录原子发布带心跳的 `companion-session.json`。命令包含 owner、session、唯一 request ID，拒绝陈旧和重复。伙伴不创建 GameDataManager 或写存档。心跳超过三秒未更新时冻结最后计时、禁用会话控制并切换 Idle。

主页面与伙伴共用仅显示的 `IFocusPresentation`。开始时记录 Cat Level，倍率为 `1 + 0.1 * (level - 1)`。基础为每完整有效分钟 0.5 EXP、每完整五分钟 1 鱼币，倍率后向下取整。货币奖励时间为每次 180 分钟、每日 240 分钟。Lv.30 的 25 分钟为 48 EXP/19 鱼币，单次理论上限 351 EXP/140 鱼币，每日理论上限 468 EXP/187 鱼币，分会话取整可能略少。会话中升级和重试不改变已记录倍率，下次会话使用新等级。

每日用量跨模式共享并归结算日。End Early 使用同一规则，有效秒数计入目标。每完整 25 分钟一券，专注每日最多三券且不按等级放大。时长选择器仍为 60/180 分钟，正计时可在达到额度后继续。成功保存后番茄钟进入休息，其他模式结束，失败等待重试。休息、取消、无效和中断无奖励。休眠或超过十秒的不可验证间隔使会话失效。旧收据不重新定价，缺失 rewardLevel 使用原等级一规则。装饰鱼最多 72 个，不限制鱼币。

## 验证入口

**Tools > Cat Quest > Verify Rewards and Focus** 检查奖励及 Dashboard。FocusImplementationVerification 增加时长边界、自定义滑条、保存失败重试、提前结束、取消、重复、任务快照、非法预设和兼容检查。独立冒烟仅在明确 `--focus-smoke`、`--smoke-dir` 参数下使用隔离存档。

Windows 集成证据在 `Library/FocusCompanionVerification`，通过真实独立进程检查置顶、悬停、拖动缩放、最小化计时及同步。旧文件不是当前通过证明。

`Temp/verify-focus-layout.request` 使用隔离存档检查背景、隐藏标题、滑条、可搜索标签、设置锁定、休息奖励及 1920×1080/1280×720。时间戳输出在 `Library/FocusLayoutVerification`，恢复环境、起始场景和 Game View。

## 子进程关闭清理

伙伴使用 `-force-d3d11`。已有 Unity 6000.3.10f1 诊断记录显示 D3D12Core.dll 在工具窗关闭时出现 0xc0000005，而 D3D11 正常退出。参数仅作用于子进程，不修改主应用图形设置。

`SpriteSkinShutdown` 在 Application.quitting 调用 2D Animation 13.0.4 的幂等 `ClearFallbackBuffer`。该包即使没有 SpriteSkin 实例也可能分配静态 ComputeBuffer，而序列帧伙伴没有对应变形系统清理者。`Assets/Scripts/UI/link.xml` 保留此内部方法供裁剪构建使用。升级到自行清理的包后应重查并移除补丁。

## 三模式与 Windows 检查

主页面、Dashboard 和伙伴一小时内显示 MM:SS，之后 H:MM:SS。正计时向下取秒，倒计时向上取秒。伙伴快照增加模式及 elapsedSeconds，同时保留 remainingSeconds，主程序与伙伴须一起重建。断连仍冻结。

领域验证覆盖四小时正计时、三小时单次奖励、跨模式日额度、重试重复、中断、旧用量和零休息重载。

```powershell
pwsh -NoProfile -File Tools/Verify-FocusModesWindows.ps1 -Executable Builds/FocusModes/CatQuest.exe
```

使用已有开发构建和隔离数据，检查主伙伴状态、保存重试、重复陈旧错误 owner、额度、心跳冻结、原生尺寸最小化及关闭。恢复 PlayerPrefs，只关闭自身进程。Windows 拒绝移动鼠标时，真实指针检查明确未验证，不算通过。输出 `Library/FocusModesVerification/native-*`。

## 番茄钟取消确认

主页面和伙伴共用 `Assets/Prefabs/Focus/FocusCancelConfirmation.prefab`，来自 Boss Skip / SimpleCasual 双按钮弹窗。默认 Keep focus，Escape 或背景关闭不改会话。弹窗期间继续计时，确认校验捕获的会话身份。会话结束、变化、切页或断连清除过期提示。主机拒绝旧番茄钟 `end` 命令，取消仍为原子无奖励结算，失败用 Retry Save，历史提前结束收据保留。

`Temp/verify-pomodoro-cancel.request` 检查隔离 UI、陈旧会话和主机防护及两种标准尺寸。`Tools/Verify-PomodoroCancelWindows.ps1 -Executable <development build>` 检查真实主伙伴确认、普通最小尺寸、错误命令、自然结束和干净关闭。仅 `--companion-smoke` 接受冒烟 UI 请求。

只查标签时，在编译后将 `tags` 写入 `Temp/verify-focus-layout.request`，检查搜索、单标签替换、键盘选择清空、重开、锁定及快照并恢复环境，不能替代完整模式反馈检查。

完整布局验证先确认升级弹窗阻挡背景，再通过最上层 EventSystem 射线点击 Continue，不关闭射线来绕过。记录模式、标签、滑条、悬停按下释放、快速输入及复位到 `interactions.txt`、`motion.csv`、`pointer-motion-*.png`。事件经 EventSystem 分发，不覆盖实际 OS 鼠标或焦点切换。

## Focus insights

从 Focus insights 打开统计。Day 按小时，Week 为周一到周日，Month 按日期，Year 按月。前后和 Today 切换周期，未来禁用。默认按标签堆叠并配颜色图例，目录重复颜色使用稳定显示颜色区分，不改保存颜色。悬停点击片段查看标签时长，图例可单选标签，All focus 恢复。Back to Focus、Escape 或切页关闭。

只统计已提交 Completed/EndedEarly。优先实际 elapsedSeconds，含超过奖励额度的时间，旧数据回退 eligibleSeconds/分钟。以 completedUtc 为结束向前推活动时长，再按本地小时、日、月拆分。统计不是奖励，不写入或重定价。运行中、取消、无效中断和休息不计。跨期会话每期计一次，先合计精确秒再显示完整分钟，少于一分钟显示 <1 min。

新会话最多一个不同标签。旧无标签归 No tag，按稳定 ID 分组，目录存在时用当前名字颜色，删除标签保留快照。旧多标签保留并在各标签计入，标签合计可能重叠，All focus 每收据只算一次。About this data 与解释区已移除，图和列表使用空间。当前周期 Today 蓝色，其他周期浅蓝。

静态资产为 `Assets/Prefabs/Focus/FocusStatistics.prefab`，嵌套在原 Focus 页。`FocusStatistics` 纯查询，`FocusStatisticsUI` 显示输入，`FocusStatisticsGraph` 批量网格柱图。按钮来自 SimpleCasual Btn_MenuButton_Rectangle01_Light 并保留字体、JuicyButton。无需图表包、迁移或重置。

编译后创建空 `Temp/verify-focus-statistics.request`，隔离领域和 Play 结果在 `Library/FocusStatisticsVerification`。恢复环境、起始场景、暂停及 Game View，不修改修复资产。指针检查是 EventSystem，不是真实 OS 鼠标。

## 伙伴输入回归

对最新 Windows 构建执行 `pwsh -NoProfile -File Tools/Verify-CompanionInputWindows.ps1 -Executable Builds/Windows/CatQuest.exe`。需可交互的 Windows 桌面：运行器发送真实键盘／鼠标输入，并实际点击任务栏按钮，不用 ShowWindow 代替恢复。它使用已解锁功能的虚构隔离存档，检查普通／最小尺寸、键盘焦点与导航、快速输入、pending 请求防重、保存失败／重试及确认框行为，并恢复 PlayerPrefs、指针和前台窗口。结果与截图在 `Library/CompanionInputVerification/native-*`；找不到任务栏目标或原生输入被拒绝均判为失败。此检查不替代完整奖励验证。
