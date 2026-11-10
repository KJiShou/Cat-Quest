# Cat Quest 设计、可用性、反馈与性能审查

**2026-11-10 Windows 保存复测后的优化：** 校验缓存改为直接复用原始文件字节，去掉每次保存的压缩／解压；仍逐字节核对主档和备份，保留原子提交、有限重试和数据恢复。约 8.3MB 隔离存档、同机 Development Player、1280×720、本轮重新测量的中位数：保存 **219.41→186.68ms（约减少 15%）**，保存加可见 Tasks 刷新 **239.58→218.36ms（约减少 9%）**。完成任务重试单次 **337.58→229.94ms**，不把单次值当作稳定统计。代价是两份校验缓存约 **15.9MiB**；完整 JSON 序列化仍在主线程，因此不是无卡顿保存。错误弹窗文案已移除异常路径；720p／1080p 实际 Player 的失败、重试、重复调用、重启恢复及恢复提示关闭均通过。玩法和手调 UI 未变。当前证据：`Library/SaveOptimization/20261110/verification.md`。以下压缩缓存及旧耗时记录属于历史版本。

**2026-11-10 大存档保存继续优化：** 保留 Unity JSON、原子提交及失败后重试语义，仅并行重叠大文件校验／压缩／临时写入，并以 8 字节单元精确比较缓存。文件替换出现 I/O 错误时，只有主档和备份仍完全未变才有界重试同一准备文件，不重跑任务或奖励。当前同机 Editor 1,000 任务／三年样本：保存 **230.89→140.90ms（约减少 39%）**，完整提交 **226.63→153.24ms（约减少 32%）**，双文件核对 **52.63→10.22ms**。大文件失败／重试／恢复、连续 Unicode 保存、外部新版文件保护、领域聚合均通过；UI prefab 哈希不变。仍保留约 77ms 的 JSON 序列化及其他同步成本，不能称为无卡顿或全异步保存。当前证据：`Library/SaveLatency/20261110/verification.md`，包含被撤销的慢实验、发现并修复的替换错误、最终结果及测量边界。下方旧数字仅作历史追溯。

**2026-11-10 后续优化与当前状态：** 普通 Tasks 浏览不再复制多年收据／occurrence，顶栏和装备摘要避免无关历史副本；History 预先计算排序键并保留原排序。主档／备份校验缓存压缩为字节，但每次仍逐字节比较实际文件，磁盘 JSON、原子保存、完整历史和奖励规则不变。新基线 → 最终 Editor 复测（1,000 任务／三年）：Tasks 刷新 **90.60→27.13ms**，History **152.62→91.68ms**，保存 **411.08→239.98ms**，完整提交 **355.91→242.49ms**；另一轮 after 分别为 50.51／148.47／366.34／404.31ms，机器负载波动明显，不能将单轮降幅当成稳定保证。校验缓存载荷 **31.7→0.46MiB**，不是应用总内存或 GC 分配量。同步大存档保存仍非零卡顿。

720p Dashboard 属性字号、奖励 hover 规则文字和两处装备空状态图标已局部改善；保留手调布局、字体、原 Boss Continue。项目自有森林演示场景清理两处失效组件；剩余 9 处在未被启用场景依赖的第三方示例中，未修改供应商原件。当前 Unity 编译、性能／保存回归、领域聚合、Tasks 四尺寸、Dashboard、Equipment、Tooltip、Guidance 检查通过；证据与完整限制见 `Library/RemainingOptimization/20261110/verification.md`。低配、功耗和长期运行仍未验证。下方历史数字与问题列表只作追溯。

**2026-11-10 保存／History／Tasks 性能复测与优化：** 同机 Editor、同一 1,000 活跃任务／三年历史夹具、预热 1 次测量 3 次：保存 **677.55→300.61ms**，完整提交 **900.39→375.68ms**，History 查询 **282.14→163.23ms**，Tasks 刷新 **255.97→85.02ms**。优化大型数据的独立副本，减少 JSON 序列化往返，并改为兼容的紧凑 JSON 存档；没有修改 UI 资产、奖励规则或历史保留范围。下文旧性能数字保留历史含义，当前数值以上述复测为准；大样本同步保存仍约 300ms，不能称为完全无卡顿。当前证据见 `Library/SavePerformance/20261110/verification.md`。

**2026-11-10 用户选择：恢复 Boss 原结果页。** Boss Victory／Defeat 恢复居中单个 Continue；撤回本轮新增的结果 Mastery 进度条、推荐说明、图标及第二个跳转按钮。胜利仍显示实际已保存的 EXP／Fish，Continue 沿原返回转场回到 Boss 预览；Master 胜利仍选择已保存下一关，不自动开战或扣票。Stage 选择、Mastery 固定领取顺序和奖励规则保持。下方 11／13 的结果引导方案是已撤回的设计尝试，不应再列为已启用功能或强制待修缺陷。本次证据索引：`Library/BossResultRestore/20261110/verification.md`。

**2026-11-10 简化本轮新增说明：** 按用户要求，让图标、布局和状态承担常规说明。装备空状态改为宝箱／锁／分类／Boss 图标、必要门槛和一个入口；Auto Select 成功后用勾选、Power 差值及原有槽位高亮反馈，选择依据在悬停／键盘焦点时显示。Boss 胜利结果复用宝箱与每五胜进度条，下一步按钮配实际装备或页面图标；只在真实解锁、失败后果等必要场景保留短文案。Focus 用时钟→Ticket 和连续进度条替代常驻规则段落，End Focus 用奖励图标和预计数值，完整票数规则可按需查看。保留字体、已有对象和用户调整的其他 UI，以及固定 Mastery 领取顺序、奖励与存档规则。当前修订的 Guidance 与 Equipment Play Mode 检查分别通过 **233 / 662 项断言**；双尺寸主界面、四尺寸导航及动作帧的实际审查和相关回归索引见 `Library/VisualSimplification/20261110/verification.md`。以下原始问题表与上一轮文字方案保留作追溯。

**2026-11-10 导航提示样式：** 7 个页面名称提示及 3 个锁定说明，已局部换为现有 `FocusCancelConfirmation/Popup/Button_USD` 使用的 SimpleCasual `Btn_Rectangle00_n_White1` 圆角矩形及向下 3px 的轻阴影。保留文字、字体、位置、尺寸、绑定与玩法。Unity 编译和四尺寸 Play Mode 导航检查通过：`Library/RemainingFixesVerification/20261109-230611-navigation-after`，692 项断言、32 张截图；详细证据在 `Library/NavHintStyle/20261110/verification.md`。早期 Editor 尺寸采样失败未计作通过，修正验证上下文后重新执行；未重新构建 Windows 玩家。

**2026-11-10 第三轮：处理剩余 08–15、17–26。** 下方原始问题表保留作追溯，不再是当前待办清单。本轮保留现有玩法、奖励、End Early、Mastery 固定领取顺序及用户调整的 UI；只扩展既有组件和必要子树。

| 原 ID | 当前处理 |
| --- | --- |
| 08、09 | 折叠侧栏增加可见的 hover／键盘页面名提示；日期提高字号和对比度并修正行高，装备属性文字局部增强。 |
| 10、15 | 背包空状态用图标、必要门槛和当前可用入口；Auto Select 按总 Power 选择，成功后显示勾选、Power 差值和槽位反馈，选择依据按需显示。 |
| 11、13 | 用户选择恢复原结果页：仅保留居中 Continue 与原结果／实际奖励；领取和进度仍通过原 Stage／Mastery 页面查看。新增结果引导已撤回。 |
| 12、14 | Focus 用时间→Ticket 与进度条、图标奖励预估和按需规则帮助解释单局票数；Tasks 教学先实际创建并成功保存，再解释 tabs。教程与语音同步。 |
| 17、18 | 新增持久化 Reduce Motion 并同步 Companion；键盘完成任务的奖励从实际操作对象出发，失败与额度用尽不播放虚假奖励。 |
| 19、20、21、24、26 | 减少重复存档解析、装备／任务／统计的无关深拷贝、重复查询与控件重建；移除 tab 每帧字符串构建和 Chat 输入时历史刷新。保留原子保存及完整历史。 |
| 22、23 | 三处预览尺寸稳定 150ms 后才重建 RenderTexture；Boss 改为异步资源加载，带等待／重试、旧请求隔离和入场保护。 |
| 25 | Windows 实测确认最小化仍约 30Hz 渲染，且隐藏的 Dashboard／Cat Home 共用森林相机仍在工作。已增加共享相机使用者管理及三个预览的最小化停渲染／还原恢复；Focus、IPC 和 Boss 模拟继续运行。最终量测与原生回归见本轮验证记录。 |

**已量测的局部改善：** 相同 1,000 任务／三年合成历史下，Editor 月历查询由 3,861ms 降至 110ms，Statistics UI 刷新由 184ms 降至 7ms。另一个各 3,000 条任务／Focus／战斗历史的样本中，装备 hover 中位数由 70.18ms 降至 1.05ms。四次侧栏切换中，三处预览的 RenderTexture 重建各降至 4 次。以上是特定操作量测，不能换算为整体 FPS；精确 GC allocation 计数器在当前 Editor 不可用。

**当前处理状态与限制（2026-11-10 最终复测）：** 原问题已按本轮及历史记录处理；同步大存档成本、第三方示例记录和未验证范围仍需明确区分。

| 优先级 | 仍有的问题或代价 | 证据与适用范围 |
| --- | --- | --- |
| P1，大历史场景 | 保存阻塞已进一步减少；仍非全异步／零卡顿。 | 最新 Windows 1,000 任务／三年样本：保存中位数 186.68ms、含 Tasks 刷新 218.36ms；序列化仍是主要成本。详见 SaveOptimization 证据。 |
| P2，性能取舍 | 以缓存内存换取保存延迟下降。 | 当前主／备份原始字节缓存约 15.9MiB，旧压缩缓存约 0.46MiB；只保留两份，不累计历史版本。不是应用总内存或精确 GC 数据。 |
| P2，可读性 | 本轮已局部改善 Dashboard 属性和奖励规则字号。 | 720p／1080p 实际画面与交互检查通过；布局、字体、图标语义和 Boss Continue 保留。 |
| P3，项目清理 | 项目自有演示场景 2 处已清理；第三方示例 9 处保留。 | 最终扫描 `Library/RemainingOptimization/20261110/unity-assets-check-final.json`；剩余资产不在启用场景依赖中，应用 UI 无缺失脚本。没有修改供应商原件来清空扫描结果。 |

**尚未验证，不等于已确认有缺陷：**

- GPU 耗时、显存、功耗和电池续航尚未直接量测。最小化后预览 RT 渲染为 0，不代表屏幕相机、主循环或后台资源使用为 0。
- 最终原生性能样本来自单台 Windows 设备的 Development build、D3D11、1280×720；每种窗口状态采样 12 秒、两轮。它不能证明 Release／低配设备表现，也不能替代数小时运行的内存增长和稳定性检查。
- 该次 Editor 运行的精确 allocation 计数器不可用，CSV 明确记为 `allocationCounterAvailable=false`、`bytesPerCall=-1`；不能宣称零 GC。
- 教程、新结果说明和下一步引导尚未做真实首次用户的理解度／操作成功率研究。
- Companion 原生输入通过的是明确激活后的鼠标／键盘流程；未激活时第一次直接点击 Pin 是否生效，尚未单独验证。

**现存证据索引：** `Temp/RemainingFixes-20261109/verification.md` 在此次复核时已不存在，不再作为可访问的唯一验收索引。下表列出仍保留的原始结果；下文其他 `Temp/` 路径保留其历史记录含义，不能据此假定文件仍存在。失败的早期运行未当作通过，也没有因索引丢失而虚构或重新生成结果。

| 验证范围 | 仍可读取的结果与附件 |
| --- | --- |
| 大历史查询／保存／UI 操作 | `Library/RemainingFixesVerification/20261109-161451-performance-after/result.txt`；其 `performance-after/` 子目录含 `metrics.csv`、`measurement-notes.txt`、`scale-1000.json`。对照量测位于 `Library/RemainingFixesVerification/20261109-154620-performance-before/performance-before/metrics.csv`。 |
| 预览／共享相机／局部画面与动态效果 | `Library/RemainingFixesVerification/20261109-171307-presentation-after/result.txt`、`checks.txt`、`shared-camera-lifecycle.csv` 及同目录截图。 |
| 运行时交互／引导／装备 | `Library/RemainingFixesVerification/20261109-164533-runtime-after/result.txt`；`Library/RemainingFixesVerification/20261109-164951-guidance-after/result.txt`；`Library/RemainingFixesVerification/20261109-162801-equipment-after/result.txt`。 |
| Windows 两轮后台量测 | `Library/BackgroundPerformance/native-20261109-171726/result.json`、`metrics.csv`、`context.json`、`measurement-notes.txt` 及各状态的 `*-render.json`。 |
| Windows 四页最小化／还原与 Boss 连续结算 | `Library/PreviewWindowsVerification/native-20261109-172107/result.json`、`boss-continuity.json` 及 `*-restored.png`。 |
| Companion 实际鼠标／键盘／任务栏交互 | `Library/CompanionInputVerification/native-20261109-173313/result.json`。 |
| Focus 原生计时／失败重试／身份防重／断连重连 | `Library/FocusModesVerification/native-20261109-173725/result.json`；同目录 `post-checks.json` 记录当时的无残留测试进程和玩家窗口偏好恢复检查。 |

**上一轮最终验收（2026-11-10 01:39 MYT）：** 对应 `Builds/SettingsVerification/Build-20261109-171416/CatQuest.exe` 的 Development build 编译成功、错误 0；共享相机生命周期、四页 Windows 最小化／还原、两轮后台测量、FocusModes 和 Companion Input 均 PASS。主窗最小化的预览 RT 渲染由约 60Hz 降至 0；Companion 最小化后也为 0，计时／IPC／Boss 结算继续。保留原有 60／30 FPS 主循环与屏幕相机。当时无测试进程残留，玩家窗口偏好已恢复。这些历史结果不代表之后的 UI 样式修改已经重新验收。

审查日期：2026-11-09（Asia/Kuala_Lumpur）。范围：当前 Unity 6000.3.10f1 工作区的实现、序列化绑定、相关产品文档，以及审查时生成的部分 1920×1080／1280×720 UI 画面。最初审查没有修改运行时代码、游戏规则、UI 资产或玩家存档。

**同日后续修复：用户已要求先处理 01–04。** 任务详细草稿在离页、切 tab、切任务及日历返回／关闭时先询问，默认继续编辑；Boss 离页复用 Stop 确认，停止结果保存成功后才导航；Focus 开始／结算失败在现有提示卡持续显示，退出说明区分 Focusing 与 Saving；Mastery 按用户确认的现有固定领取顺序统一为 5 胜武器、10 胜护甲、15 胜头饰，并同步两句导师语音与教程文档。玩法、奖励、领取条件及手动调整的 UI 资产保持。下表保留最初发现，01–04 不再代表当前未修复问题。当前验证记录位于 `Temp/PriorityFixes-20261109/verification.md`。

**同日第二轮修复：按本文建议顺序处理 16、05、06、07。** 侧栏只保留一个动画并从当前宽度继续；Tasks 按 tab 保留会话内搜索、筛选、选中项与滚动，Calendar 切换／保存保持列表状态独立，默认 tab 仅用于启动后首次进入；Daily 删除在所有入口说明停止未来重复并保留历史与奖励；装备详情、Mastery 与确认弹窗统一 Tab／Shift+Tab／Escape、默认取消及关闭后焦点恢复。保留玩法和用户调整的 UI 资产，Settings 教程与配套语音同步更新。上述四项不再代表当前未修复问题；本轮验证与边界记录位于 `Temp/PriorityFixes-Round2-20261109/verification.md`，最终四项 Play Mode 结果为 `Library/UsabilityRoundTwoVerification/20261109-152526-after` PASS。原表保留用于追溯。

**总体判断：优先改善操作可靠性、结果解释和下一步引导。** 任务 → Focus → 成长 → 装备 → Boss 的循环已经存在，也已有奖励飞行、升级庆祝、战斗打击和领取特效。新增大量粒子的收益，小于解决草稿丢失、退出战斗、错误提示和成长反馈断层。

证据标记：**代码**＝实现或绑定支持，未必已交互复现；**画面**＝本轮实际截图观察；**假设**＝需用户测试或 Profiler 验证。P1＝优先处理或测量，P2＝下一轮，P3＝后续打磨。性能 P1 不代表已经测出严重卡顿。本文行号对应审查时的文件。

**先处理的四项**

| ID | 优先级／证据 | 当前问题与影响 | 建议 | 实现证据 |
| --- | --- | --- | --- | --- |
| 01 | P1／代码 | 任务详细编辑时切侧栏或任务 tab，直接取消未保存草稿。快速新增却会保存，两个流程的预期不一致。 | 仅在内容变化时提供“保存／放弃／继续编辑”，或会话内保留草稿。保存失败时保持原输入。 | `Assets/Scripts/UI/TaskSystemUI.cs:131`、`:186`、`:460`；`Assets/Scripts/UI/MainMenuNavigation.cs:48`。 |
| 02 | P1／代码 | Boss 战斗中切页会触发 `Stopped`，已消费的 Ticket 不退；显式 Stop 有确认，侧栏切页没有。想查看 Tasks 也可能意外中止。 | 离页复用 Stop 确认，说明票已使用，默认继续战斗。保留有效停止消耗票与技术中断恢复的区别。 | `Assets/Scripts/UI/MainMenuNavigation.cs:43`；`Assets/Scripts/UI/BossPageUI.cs:166`、`:347`、`:353`。 |
| 03 | P1／代码与绑定 | Focus 开始保存失败，只写 Controller.Message，页面没有可见失败原因；保存失败的通用通知又会在 4 秒后消失。用户可能只觉得 Start 没反应。Saving 状态退出时，文案说取消且无奖励，实际执行的是重试结算。 | 在现有设置卡持久显示“未开始／尚未保存”、具体原因与重试；成功后清除。退出说明按 Focusing／Saving 分开。保留已退役天空文字的现状。 | `Assets/Scripts/Tasks/FocusController.cs:64`；`Assets/Scripts/UI/FocusPanelUI.cs:249`、`:325`；`Assets/Prefabs/Canvas.prefab:97526`、`:117090` 的 message 为空（结束前重新核对）；`Assets/Scripts/UI/FocusExitGuard.cs:23`、`:30`。 |
| 04 | P1／代码、教程与规则文档 | Mastery 教程让人理解为每到 5／10／15 胜可选一个未拥有部件，但实现固定为武器／护甲／头饰依次解锁。AGENTS 与较新的装备文档也存在表述差异。 | 先明确认可的规则，再统一教程、领取条件和文案。若保留固定顺序，清楚写“5 胜武器、10 胜护甲、15 胜头饰”。这不是擅自改变奖励的建议。 | `Assets/Scripts/Tasks/TaskService.Equipment.cs:17`；`Assets/Scripts/UI/EquipmentClaimUI.cs:35`；`Assets/Scripts/UI/TutorialController.cs:239`、`:272`；`EQUIPMENT.md:11`。 |

**操作体验与可读性**

| ID | 优先级／证据 | 当前问题与影响 | 建议 | 实现／画面证据 |
| --- | --- | --- | --- | --- |
| 05 | P2／代码 | 返回 Tasks 会回到默认 tab，切 tab 会清空搜索、难度、tag 筛选与选中项。用户去 Focus 再返回，需要重新找任务。 | 首次进入使用设置默认值；本次会话内保留各 tab 查询、选中项和滚动位置。删除任务等状态变化后再校正失效选择。 | `Assets/Scripts/UI/TaskSystemUI.cs:129`、`:186`。 |
| 06 | P2／代码 | 删除 Daily 的确认只说删除任务、保留历史，没有说明未来重复也会停止。实际删除整个任务模板。 | 显示任务名称，并写明“停止此重复任务及未来出现，保留历史”。无需为了修正文案新增复杂的单次删除机制。 | `Assets/Scripts/UI/TaskSystemUI.cs:337`；`Assets/Scripts/UI/TaskSystemUI.Calendar.cs:20`；`Assets/Scripts/Tasks/TaskService.cs:210`。 |
| 07 | P2／代码 | 装备 hover 能用 Esc 关闭，正式详情／领取界面的退出方式与 Settings、Focus 确认不一致。 | Esc 关闭最上层弹窗并恢复原焦点；检查 Tab 不落到背景。参考已有键盘行为，避免新建模态框架。 | `Assets/Scripts/UI/EquipmentPageUI.cs:128`；`Assets/Scripts/UI/EquipmentItemDetailUI.cs`；对照 `Assets/Scripts/UI/FocusCancelConfirmation.cs:43` 与 `Assets/Scripts/Settings/SettingsPanelUI.cs:156`。 |
| 08 | P2／代码＋画面，hover 待实测 | 折叠侧栏只剩图标；现有 tooltip 主要解释锁定原因，解锁后隐藏。Cat Home 用背包图标，也需要学习其含义。 | 折叠时 hover／键盘 focus 显示页面名；保持展开侧栏已有标签。 | `Assets/Scripts/UI/SidebarNavigationButton.cs`；`Assets/Scripts/UI/FeatureLockUI.cs:47`；本轮 Dashboard／Tasks／CatHome 截图。 |
| 09 | P2／画面判断 | 720p 的任务日期、Dashboard 角色属性、部分奖励说明字号明显小于主要图标；1080p 的日期和属性仍是弱信息层。难以快速扫读与比较。 | 局部提高关键文字的最小显示尺寸和对比度，重新分配图标与文本空间。保留 Fredoka、现有字体选择和 SimpleCasual 组件；不做全局字体替换。 | 本轮 `Tasks-1280.png`、`Dashboard-1280.png`、`CatHome-1280.png`，并与 1920 版本比较。此项是视觉审查判断，未做读速用户实验或逐元素对比度测量。 |
| 10 | P2／代码＋画面 | 空背包用 24 个带“＋”的占位格；解锁或获取方式放在底部一句话，缺少直接入口。“＋”也可能让人以为能点击添加。 | 空背包中央说明当前下一步：领取 Daily Reward／挑战 Boss；提供通往已有页面的按钮。只展示必要占位格或降低其视觉权重。已有说明需增强位置与行动性，并非完全没有空状态。 | `Assets/Scripts/UI/EquipmentPageUI.cs:66`、`:74`、`:89`；本轮 `CatHome-1280.png`、`CatHome-1920.png`。 |

**玩法循环与成长反馈**

| ID | 优先级／证据 | 当前问题与影响 | 建议 | 实现证据 |
| --- | --- | --- | --- | --- |
| 11 | P2／代码，用户影响待试玩 | 战败只说 “Train and try again”。同一有效配置下战斗确定，属性不变再次入场不会改变结果，可能反复耗票却没有学习。 | 结果给一条具体下一步，例如升级已有武器、领取已达成部件或提高 Cat Level，并提供 Cat Home 入口。准备度提示可以解释差距，不必显示未经验证的胜率。 | `Assets/Scripts/UI/BossPageUI.cs:242`、`:248`、`:255`；`Assets/Scripts/Battle/BattleRules.cs:48`。 |
| 12 | P2／规则确认、理解成本为假设 | Focus 每日目标可累加短局，Ticket 按单局完整 25 分钟计算。两次 15 分钟可以完成每日目标，却没有 Ticket，界面上的奖励数字不足以解释这个差别。 | Ticket 附近说明“本次每满 25 分钟获得 1 张”，展示距下张票的时间；End Focus 确认显示预计结算。保留 End Early、每日／单局上限及当前数值。 | `Assets/Scripts/Tasks/TaskService.Rewards.cs:98`、`:208`、`:244`；`Assets/Scripts/UI/FocusPanelUI.cs:304`；`Assets/Scripts/UI/FocusCancelConfirmation.cs:23`。 |
| 13 | P2／代码 | 胜利结果突出 EXP／Fish，但 mastery 或后续 Boss 解锁要再打开 Stage／领取页才看见。重要胜利的长期意义不够直接。 | 已成功结算后显示一行进展，如“Cyclope 5/15，武器可领取”或“Orc 已解锁”，加一个入口和短暂局部高亮。完整 mastery 进度已有，不必重做进度系统。 | `Assets/Scripts/UI/BossPageUI.cs:125`、`:248`、`:255`；`Assets/Scripts/UI/EquipmentClaimUI.cs:32`。 |
| 14 | P2／设计假设 | Tasks 教学先解释多个 tab，第六步才新增、第七步才保存。虽然已有 Skip／Back，首次获得实用价值可能偏慢。 | 测试“先创建第一项真实任务，再按需解释 tab”的渐进引导；保留完整帮助入口。以首次成功保存时间、跳过率和理解度判断，不直接认定现有教学无效。 | `Assets/Scripts/UI/TutorialController.cs:165`、`:181`、`:187`；Focus 教学从 `:199` 开始。 |
| 15 | P2／代码 | Auto Select 按 Power 选择装备，成功后仅刷新；用户既不知道是否换了装备，也可能把它理解为针对当前 Boss 的最佳策略。 | 标注“按总 Power 自动装备”；结果显示“更换 2 件／已是当前推荐组合”，短暂高亮变化槽位并显示属性差值。无需增加大粒子爆发。 | `Assets/Scripts/Tasks/TaskService.Equipment.cs:35`、`:41`；`Assets/Scripts/Battle/BattleRules.cs:10`；`Assets/Scripts/UI/EquipmentPageUI.cs:187`。 |

**动画、粒子与反馈**

| ID | 优先级／证据 | 当前问题与影响 | 建议 | 实现证据 |
| --- | --- | --- | --- | --- |
| 16 | P2／代码与绑定；运动待复现 | 侧栏检查 `_animationRoutine` 以停止旧动画，却未保存 `StartCoroutine` 的返回值；快速连续开合会让多个协程同时写宽度。当前 prefab 开启动画，时长 0.3 秒。 | 只保留一个协程，从当前宽度继续；用 unscaled time 并在禁用时收敛。检查快速输入、失焦、关闭与重开。 | `Assets/Scripts/UI/ResisesSideMenuInLayoutGroup.cs:79`、`:84`、`:99`；`Assets/Prefabs/Canvas.prefab:114182`、`:114207`（结束前重新核对）。 |
| 17 | P2／功能缺口确认，强度待观察 | 有按钮弹性、战斗震屏／hit stop、升级旋转光芒，但 Settings 没有减少动态效果选项。生产力场景可能需要更安静的表现。 | 增加“减少动态效果”：降低弹跳、震屏和重复粒子；保留明确的文字、数值、选中态和成功提示。不能据静态截图断言现有效果过强。 | `Assets/Scripts/Settings/AppSettings.cs:6`；`Assets/Scripts/UI/JuicyButton.cs:46`；`Assets/Scripts/Battle/BossBattleActors.cs:82`；`Assets/Scripts/UI/LevelUpCelebrationUI.cs:68`。 |
| 18 | P3／代码、设计建议 | 键盘完成任务时，奖励飞行默认起点可能在屏幕中心，而不是刚完成的任务行；反馈与操作对象的联系较弱。 | 在成功提交后把任务行／完成按钮作为显式起点，辅以轻量勾选过渡；继续复用飞行对象池。任务完成与实际奖励为 0 的状态应有不同说明。 | `Assets/Scripts/UI/RewardFlightUI.cs:117`；项目当前 `SetRewardOrigin` 调用位于 `Assets/Scripts/UI/BossPageUI.cs:232`。 |

针对“哪里缺 animation／particle／feedback”，最有价值的顺序是：**先补失败反馈（03）、里程碑反馈（13）、换装反馈（15），再修动画稳定性（16）和任务反馈起点（18）**。任务记录、存档结果、奖励额度与结算仍由现有服务负责，动画只能展示已经提交的结果。

**性能：当前代码支持的量测与优化清单**

这些是优化候选，未进行本次 Windows Player Profiler 采样。没有足够证据声称整体性能差、存在内存泄漏，或给出改善百分比。11 月 2 日的历史性能报告只作为背景。

| ID | 优先级／证据 | 当前成本／风险 | 建议与量测方式 | 实现证据 |
| --- | --- | --- | --- | --- |
| 19 | P1 先测／代码 | 每次 Commit 深拷贝整份存档；TrySave 再读取／解析主文件和备份，最后序列化与写盘。任何提交的成本都会受长期历史影响。 | 先分段量测 Copy、校验读取、序列化与磁盘替换；减少重复解析与无关复制。任何异步方案必须保留串行提交、原子保存、失败回滚和成功后反馈。 | `Assets/Scripts/Tasks/TaskService.cs:77`、`:82`；`Assets/Scripts/Tasks/PlayerDataStore.cs:80`、`:87`、`:94`、`:95`、`:114`、`:128`。 |
| 20 | P1 先测／代码 | 装备 hover 通过 `service.Snapshot` 复制任务、Focus 和战斗历史；升级等成功时也存在事件刷新后再次显式刷新。 | 查询装备所需的小快照或按数据版本缓存；一次提交使用一个刷新入口。用大历史存档连续滑过装备，测 CPU／GC，而非仅测空存档。 | `Assets/Scripts/UI/EquipmentPageUI.cs:55`、`:115`、`:132`、`:179`、`:188`；`Assets/Scripts/Tasks/TaskService.cs:46`。 |
| 21 | P2／代码 | Tasks 已虚拟化显示，但数据查询仍可能复制全量数据、逐任务扫描 occurrences；Calendar 扫描历史。长期使用成本仍会增长。 | 一次查询建立 taskId＋day 索引，先按日期范围过滤，再建视图。用 100／1000 活跃任务及 1／3 年合成历史测 Today、History、搜索和翻月。不要以删除历史代替优化。 | `Assets/Scripts/Tasks/TaskService.cs:306`、`:320`、`:322`；`Assets/Scripts/Tasks/TaskService.Calendar.cs:37`、`:53`、`:65`、`:82`。 |
| 22 | P2／代码，触发频率待测 | 三处预览在整数像素尺寸变化时立即 Release／Create RenderTexture，窗口拖动和侧栏动画可能触发连续重建。 | 加尺寸档位或短暂 resize debounce，稳定后重建；拖动时临时缩放旧 target。测重建次数、显存峰值、最长帧和清晰度。现有尺寸上限应保留。 | `Assets/Scripts/UI/PreviewPixels.cs:7`；`Assets/Scripts/UI/CharacterHomePreview.cs:100`；`Assets/Scripts/UI/FocusScenePreview.cs:82`；`Assets/Scripts/UI/BossArenaPreview.cs:76`。 |
| 23 | P2／代码 | Boss 首次换场仍同步加载并 Instantiate，可能有点击后的等待；Focus 已有异步加载。 | 沿用项目已有异步请求思路，显示加载状态并防止旧请求覆盖新选择。测冷启动 Boss 1／2／3；不要为消除卡顿无条件缓存所有场景。 | `Assets/Scripts/UI/StagePrefabResource.cs:10`；`Assets/Scripts/UI/BossArenaPreview.cs:30`、`:38`、`:44`；对照 `Assets/Scripts/UI/FocusScenePreview.cs:40`。 |
| 24 | P2／代码 | Focus Statistics 翻页复制整份存档、重新计算颜色映射并重建标签按钮。只是换一周也处理无关历史。 | 仅查询 session／tag 数据，缓存颜色表并按 ID 复用按钮；测长历史的周／月／年视图。保留时间边界和 End Early 统计规则。 | `Assets/Scripts/UI/FocusStatisticsUI.cs:65`、`:79`、`:118`；`Assets/Scripts/Tasks/FocusStatistics.cs:80`。 |
| 25 | P2 先测／假设 | 前台 60／后台 30 FPS 已实现；长期最小化、同时打开 Companion 时是否仍有不必要 CPU／GPU／功耗，需要实测。 | 比较前台、失焦可见、最小化，以及 Companion 显示／最小化；仅暂停不可见的装饰渲染，保留主 Focus 时钟、结算和通信响应。不能仅凭源码断言 Unity 最小化后仍完整渲染。 | `Assets/Scripts/GameDataManager.cs:74`；`Assets/Scripts/UI/CatQuestBootstrap.cs:9`；`Assets/Scripts/UI/CompanionWindowUI.cs:39`。 |
| 26 | P3／代码 | TaskTabsLayout 每帧在早退前 `ConvertAll + string.Join`；AI 每次输入触发遍历已有消息的 Refresh。 | tabs 比较缓存计数或使用 dirty 标记；分开输入按钮状态与消息刷新。长 Chat 再评估分页；已有大小上限和分批构建，不能称为完全无界。 | `Assets/Scripts/UI/TaskTabsLayout.cs:13`、`:21`；`Assets/Scripts/AI/AiChatPage.cs:58`、`:110`、`:134`；`Assets/Scripts/AI/AiChatSession.cs:34`、`:54`。 |

性能验收应在目标 Windows 设备的 Development Player 上进行，区分冷加载／热运行、帧时间峰值／稳定帧、GC Alloc／进程内存，避免把 Editor 开销当成应用成本。方法参考 [Unity 性能分析建议](https://unity.com/how-to/best-practices-for-profiling-game-performance) 和 [Unity UI 优化指南](https://unity.com/how-to/unity-ui-optimization-tips)。源码定位不能替代量测。

**已有工作应保留**

- 手动任务离线可用、任务／Focus／Boss 的本地结算、失败重试及防重复规则。
- End Early 与 Cancel／中断的既定区别；每日奖励、等级奖励和额度 tooltip。
- JuicyButton 的 hover、press、键盘 submit 与复位；奖励飞行对象池；升级庆祝；装备升级／领取特效；Boss 打击、伤害数字、音效、胜负演出与 Skip。
- 任务可视行复用、隐藏页退订、Focus 计数缓存与按秒更新、聊天增量构建、排行榜分批构建、预览分辨率限制及按需加载。
- 当前 Companion 使用命名管道和变更快照。旧文档里持续文件心跳 I/O 的描述不能当作当前生产路径缺陷。
- 用户的 Inspector 调整、Fredoka 字体与现有 SimpleCasual 组件。后续改进应局部扩展项目 prefab。

**本次画面参考与验证边界**

视觉参考为 SimpleCasual `Prefabs/Prefabs_DemoScene_Panels/Light/Lobby.prefab`、`Light/Equipment.prefab`，以及 `Preview/014_Lobby.png`、`Preview/038_Equipment.png`；按钮补充检查 `Prefabs/Prefabs_Component_Buttons/Button_Round01-Group.prefab`。复用目标是当前 `Assets/Prefabs/Canvas.prefab` 内的页面，以及 EquipmentInventoryUI、JuicyButton 等已有项目组件。这次没有实施视觉修改，也不宣称所有页面通过设计语言验收。

在已打开的 Unity 实例中，通过既有 `Temp/verify-button-pages.request` 启动一次 ButtonPagesVerification，开始于 **2026-11-09 22:06:06 MYT**，使用其隔离任务存档／AI 目录与自动恢复逻辑。它遍历页面，生成两个尺寸的截图并模拟按钮状态；部分 Boss 结果界面是直接打开面板，不能当作真实战斗结算覆盖。

实际打开检查了本轮 Dashboard、Tasks、Focus、Cat Home 的 1080p／720p 图，以及 Boss 720p、任务编辑 720p、Focus 取消确认 720p、Settings 720p。发现了第 09、10 项的视觉问题；截图不能证明动画节奏、声音效果、真实鼠标／键盘通路或所有空／错误状态正确。

**总测试结果未确认。** 共享 `Library/ButtonPagesVerification/result.txt` 随后于 **22:09:29 MYT** 被后续运行重新写为 RUNNING，本轮最终 PASS／FAIL 未独立保留，不能将共享文件中的后续结果认作本轮结果。没有重复启动或中止后续运行。已按本轮时间窗保存 139 张当时仍可取得的截图和时间清单至 `Temp/DesignReview-20261109/captures/`、`capture-manifest.csv`；这不表示逐张完成视觉验收。

Unity compilation 未单独重新验证；本轮 Play Mode 页面检查已执行但总结果未确认。Windows build／原生 Companion 窗口行为／当前性能数值／实际运动和声音质量未验证。没有运行 domain aggregate，也没有故障注入复现 01–04。截图和证据保存在忽略目录，历史 PASS 文件未作为本次通过依据。

工作区在审查开始时已有第三方 Atlas、URP 设置和 ProjectSettings 修改；审查期间又出现其他工作对 ButtonPagesVerification、Button_Normal、Canvas 与 SidebarNavigationButton 的修改。本次只新增本文，未回滚或修理这些改动；截图记录的是捕获时的版本。后续共享运行在 22:13:27 MYT 记录 `Button_Global` 缺少 Animator 的 MissingComponentException，该运行与同时进行的按钮改动需另行核对，不能作为本轮通过证据。

执行了 `git diff --check`，返回 2：工作区非本审查编辑的 `ProjectSettings/ProjectSettings.asset:299` 存在行尾空白，未擅自修复。新增报告没有行尾空白，63 处提取到的源码路径／行号均检查存在且未越界。状态比较和检查输出位于 `Temp/DesignReview-20261109/git-status-before.txt`、`git-status-after.txt`、`diff-check.txt`。没有提交 Git commit。

**建议执行顺序与后续验收**

1. 先处理 01–04：分别检查保存失败保留草稿、战斗离页确认与票数、Focus 错误／重试／退出、5／10／15 胜教学与领取一致性。
2. 修 16 的动画竞争，同时完成 05–10 的高频操作体验；检查重复输入、Esc／Tab、失焦、返回页面和小窗口。
3. 优先量测 19、20，再决定性能改动；保留原子保存，不用历史删减或奖励提前播放换响应速度。
4. 用 11–15、18 补齐“本次发生了什么、下一步做什么”的反馈，最后根据实际观感调整粒子与减少动态效果选项。

任何共享导航／响应式布局修改都应再检查 1024×768／2560×1080 和侧栏展开／折叠；局部 UI 修改至少检查 1080p／720p 的实际渲染及交互。动画需 Play Mode 观看或短录像，性能需独立量测，不能用按钮几何检查互相代替。
