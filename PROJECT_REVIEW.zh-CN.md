# Cat Quest 项目审查历史，2026-10-20

[English](PROJECT_REVIEW.md) | [简体中文](PROJECT_REVIEW.zh-CN.md)

> 本文保留当时的决定、发现、后续修复和证据路径，另含 2026-10-31 生成器退役补充。不是当前版本的验收报告。已被后续修复解决的发现不得重新当作未实现功能。

## 决定与范围

用户批准项目规则、已确认无用的自有文件清理及小型验证工具修复，保留玩法、手工 UI、玩家存档、第三方包和 Unity 设置。End Early 继续按当时额度结算有效时间并成功保存后休息，Cancel 和无效中断无奖励。

AGENTS.md 为流程测试依据，.impeccable.md 分开全局 SimpleCasual 与伙伴布局。审查使用 Unity 6000.3.10f1 和既有 Editor 验证，不引入框架或 asmdef。

## 已完成清理

| 删除的资产及对应 .meta | 当时依据 |
| --- | --- |
| `Assets/Scripts/UI/UISpriteAnimation.cs` | 无外部类、路径、GUID 引用或自动注册 |
| `Assets/Generated/Previews/DashboardLakeIsland.png` | 无名字、路径、GUID 引用，其他在用预览保留 |
| `Assets/Scripts/UI/CompanionHeaderIcon.cs` | 仅旧 CompanionWindowDesign 私有 Icon/WindowButton 助手使用，一起删除 |
| `Assets/Editor/EquipmentPageSetup.cs` | 无外部调用或 GUID，Canvas 已保存 CatHome/Boss、Dashboard 三槽、宝箱图标和剑效绑定 |
| `Assets/Editor/StatusUserSetup.cs` | 无外部调用或 GUID，Canvas 已有 UserProfile 和四个 CatStatusUI 引用 |

删除前再次检查代码及序列化 GUID。装备指南不再指向退役请求，没有重建预制体、场景或保留的 metadata。清理前 Canvas SHA256 为 `EA9A29FDC2B742B5D63466924B383CFA18D4FF06697FEF17693EC3C703ED5FCE`。

## 验证工具修复

- CharacterHomeVerification 记录后传播打开验证失败，内部 RunForScene(Scene) 允许检查临时畸形场景而不改保存场景。
- RewardSystemVerification 保留 public Run()，新增内部 Run(Action) 检查子失败传播，不能假报 ALL PASS。
- BossPageVerification 将场景截图写到 `Library/BossPreviewVerification/ForestBossArena.png`，不改 Assets 原图。
- TaskTabsVerification 移除会重写 Canvas 的旧 setup-task-tabs.request 分支。
- DashboardRewardPlayVerification 通过当前侧栏返回并断言目标，不点已隐藏旧返回按钮。
- 新 Dashboard 检查发现旧断言要求横向 hover 放大，但 JuicyButton 对宽遮罩行有意夹住横向比例。改查纵向抬升伸展、有界运动、横向容纳及移开取消选择复位，不改运行时动画。

新增接口仅为 Editor 内部测试边界，运行 API、存档、奖励和产品 UI 不变。

## 旧生成器退役，2026-10-31

删除五个 Dashboard/Focus/Companion/Boss Build/Rebuild 菜单、请求处理及仅生成助手，删除四个过时脚本和 metadata。保留原文件中的 Boss 场景同步及 Windows 打包，保存 UI 与运行行为不变。

证据为 `Temp/GeneratorCleanup/` 的删除 GUID 引用、前后资产哈希及 unity-check.txt。CharacterHome、五预制体缺失脚本、Focus/Boss/Companion 绑定、战斗控制器和 Bootstrap 通过。当时更广引用扫描发现既有缺失 Sprite：FocusPageStage 的 `Firefly large.ShapeModule.m_Sprite` 和 ForestBossStage 的 `Background trees_53 (1).m_Sprite`。资产未变，不将其算通过或顺便修复，探针移除。此次 Editor 清理未执行 Play、原生伙伴或 Windows 打包。

## 保留工具及当时后续发现

这些记录不是授权重写页面：

| 发现 | 处理 |
| --- | --- |
| 旧整页工具删子对象或用新根覆盖 | 已在 2026-10-31 退役，保留资产，定点改组件 |
| Setup 文件可能仍有实际职责 | 保留 Boss 场景保存同步、Windows 构建、动画烘焙和验证，CompanionPageSetup 保留 Temp/build-focus-windows.request |
| 当时 EquipmentPageUI.Awake 构建静态 UI | 当时建议下一次修改才持久化受影响部分，不借清理重建。之后已迁移的现状见装备指南 |
| 仅共用图片不足以匹配组件 | 当时装备普通弹窗与文字 x 关闭需对照 Light Equipment/popup，不能全局换字体 |
| 几何测试不能证明风格运动 | 改按钮、Toggle、Slider 需新渲染、运动与状态覆盖，复用反馈 |
| Task Tabs 窄宽屏存在问题 | 后续授权修复 1024×768 展开时 Pet Widget 裁切和 2560×1080 收起时页签遮挡 |
| 当时伙伴缺负命令专测 | 修改原生协议时补陈旧重复错误 owner 与 Windows 冒烟 |
| 旧 Play 流程恢复不完整 | 有界监督，保存恢复环境与 Game View，隔离存档，后续整理生命周期，不并行验证 |

保留 TaskUIBuilder、JuicyButtonsInstaller、SpriteSkinShutdown、StoryStepDrawer、IFocusPresentation、FocusBuildSmoke，它们有运行调用、自动注册或编辑器发现。保留源场景、Resources、Spine 示例和第三方，未建立完整依赖图不能删。

## 当时清理验收

证据在 `Library/CleanupVerification/`，其他文件除明确关联均为历史，输出被 Git 忽略。

- Unity 编译并加载临时验收探针，未观察到 C# 失败。
- integrity.txt 检查 24 预制体、3 启用场景、2,895 对象，无 Missing Scripts 或损坏 MonoBehaviour 对象引用。
- failure.txt 用空临时场景使真实 CharacterHome 和 Reward 聚合报错抛出，预期失败详情在 expected-child-failure.txt、expected-aggregate-failure.txt，不损坏保存场景。
- equipment.txt 的 EquipmentVerification 及 Battle/Focus/Reward/Task/CharacterHome 链通过。
- dashboard.txt 的两尺寸 Play 通过悬停复位、领取、Focus 快捷、侧栏、休息取消、等级 HUD、重载，截图 `Library/DashboardRewardPlayVerification/9fdeaaf44e6f4471b4404d40417d51ad/`。
- boss.txt 的两尺寸动画、离页重开、票数不变通过，截图在 Library/BossPreviewVerification，原图 SHA256 保持 `0EA901EAA9CD37EFD1EEEF09EFAC25D4F181C6D9AB7D63B815E0542913EB10DB`。
- tabs.txt 的四尺寸双侧栏自动检查通过，**不等于全视觉通过**，图片揭示上述两个缺陷，证据为 tabs-1024x768-expanded.png、tabs-2560x1080-collapsed.png。
- 当时确实打开比较 Dashboard 普通悬停、Boss 和 Task Tabs 图片，但没审计所有页面。
- restore.txt 恢复起始场景、隔离存档环境、Game View 尺寸及自定义数，Editor 停在 CharacterHome 且不脏，CleanupVerificationProbe 和 meta 移除。
- final-compilation.txt 记录移除后最终 Editor 重编译，随后确认编译完成和 Play 停止，diff 检查通过，Canvas 哈希不变，无场景动画包配置变化。

这些 Editor 修复不要求 Windows 构建，原生伙伴和 Windows 未验证，也不声称所有页面符合设计。

## 后续一：响应式顶栏与任务内容

用户另行授权修复两个视觉缺陷，产品改动仅限 `Assets/Prefabs/Canvas.prefab` 布局，不改运行脚本、源场景或供应商。

- 顶栏 UserProfileGap.minWidth 180→40，两货币卡 minWidth 347→300。其余 preferred、Pet Widget、头像字体图形、比例边距间距保留，1024×768 展开时能缩小。
- TodayQuest、Content、TaskList 启用子高度控制，前二启用扩展，为原 100 高 Title 加固定 LayoutElement，为滚动区加弹性 LayoutElement，保留 preferred 高度。
- TaskDetails 只加嵌套 Canvas 高度控制、弹性视口和不受限页脚垂直 fitter 覆盖，保持长内容可滚动和按钮可见，源 TaskDetails.prefab 不改。
- 用 Unity API 定点修改，两次应用哈希相同。审计保留 3,596 fileID、全部名字引用、973 旧覆盖，只加两 LayoutElement 与五项详情覆盖。规范化两条新空 m_Name 的空白以通过 diff。

参考为 StatusBar_Group_Light_ColorButton、Toggle_Switch_Light、Light/Lobby UserInfo、Light/Equipment TabMenu。保留 Fredoka、自定义货币头像、主题、圆角、浅边阴影、图标比例和选中线。

TaskTabsVerification 增加顶栏控件标题可见性、后代重叠及真实射线，四尺寸双侧栏、0/9999 余额长名字、五页签角中心与键盘、hover/press/release、禁用重启、关页重开、模拟失焦及快速侧栏。恢复环境场景尺寸数并有期限，用 Canvas.pixelRect/根投影检查目标尺寸，避免 Editor Screen 不一致。

证据在 `Library/ResponsiveUIVerification/`：

- baseline-regression.txt 在修改前拒绝旧 Pet Widget 裁切。
- probe-integrity.txt 检查 943 对象、24,082 引用，无缺失损坏，CharacterHome 和当时六导航页绑定通过。
- tabs-result.txt 于 2026-10-20 14:04:44 UTC 完成，八尺寸侧栏组合可见、重叠、射线、键盘反馈通过，实际图及参考已查看，image-sizes.txt 独立核对尺寸。
- content-wide-result.txt 于 14:08:31 UTC 检查百字标题、多行描述、16 子任务、十标签、超宽详情编辑、滚底、页脚、空标题失败及重试，新图确认内容不盖页签。
- content-narrow-editor-existing-failure.txt 在窄详情容纳、滚动及页脚通过后，发现独立既有编辑横溢出。最小宽 680+620+20+44=1364 单位，需要 840.01 像素，超约 45.1。基线相同，长子任务也重叠，当时未修编辑宽或字体，未称视觉通过。
- task-flow-result.txt 于 14:09:28 UTC 的既有完整 Play 通过导航、添加编辑取消、标签日期时间、长内容、完成撤销历史、滚动删除和隔离持久化。
- probe-restore.txt、probe-status.txt 恢复环境且场景不脏，两个探针及 meta 移除，快照仅留忽略目录。
- final-verification.txt 于 14:11:00 UTC 最终重编译，编译完成 Play 停止，diff 通过，无场景原包 Packages/ProjectSettings 改动，Canvas SHA256 保持 `3D1042CC537AF254B7C7C7CD8772A07257F4F9A6086F2A3518D675307F30ACC0`。

原生伙伴和真实 OS 焦点未测，只检查 Pet Widget 开关边界射线未启动窗口，失焦通过现有回调模拟。序列化布局修改不要求 Windows 构建。

## 后续二：窄任务编辑器与子任务换行

用户接着授权剩余横溢出及长子任务重叠，解决前段两个未结发现，失败记录仍保留历史。

- Canvas 任务表头 Search/Filter 横组控制子宽，Search preferred 300、minimum 220，Filter 80，不改编辑器最小首选宽、排布、字体图形。
- 嵌套 TaskDetails 的 SubTasks/list/首模板行使用原生宽高布局，文本弹性 LayoutElement 让 TMP 报换行高度，勾选框固定 30×30，禁用与父级竞争的列表垂直 fitter，保留间距颜色对象，无新运行时尺寸系统。
- ResisesSideMenuInLayoutGroup.LateUpdate 在容器变宽时按当前侧栏更新内容宽，修复不切 Toggle 时旧页宽残留，沿用原组件尺寸动画。
- TaskTabsVerification 注入短长及百字子任务，检查文本高度、不重叠、编辑搜索边界和侧栏不变时缩放，保留旧交互断言。

参考 InputField_Light_Group、Toggle_Check_Light、Light Mission，四尺寸实际详情编辑图已看，圆角边阴影、配色、图标和 Fredoka 保留，保存 hover/press 与 Play 检查表明 JuicyButton 动画复位且命中区稳定。

证据在 `Library/TaskContentLayoutVerification/`：

- baseline-regression.txt 修改前在 1920×1080 原长子任务失败。
- unchanged-sidebar-resize-failure.txt 记录不切侧栏时内容宽旧值，再修复原 resizer。
- content-result.txt 于 2026-10-20 14:26:20 UTC，四尺寸双侧栏通过 16 混合子任务、百字无断词、多行十标签、行高、不重叠、真实勾选页脚、滚底、保存反馈、键盘空标题失败和重试，含顶底、勾选、悬停按下错误图。
- preservation.json 保留 3,598 序列化记录、原名和对象引用、978 覆盖，只加 11 预期嵌套覆盖、两 LayoutElement 及两个 Unity 对既有对象生成引用。
- patch-idempotence.txt 两次定点应用哈希相同，不替换场景、源详情、供应商、字体或旧 meta。
- tabs-result.txt 于 14:28:14 UTC 通过长文、窄编辑、空历史、射线键盘、禁用重启、模拟失焦、重开、快速侧栏及不变侧栏缩放。
- task-flow-result.txt 于 14:29:10 UTC 既有完整任务 Play 再通过编辑保存取消、确认添加、标签日期时间、子任务滚动、完成撤销和隔离存储。
- probe-integrity.txt 的 943 对象、24,092 引用无缺失损坏，当时六页导航通过。
- image-sizes.txt 的 64 张图尺寸正确，全部隔离存档，原生 Windows/伙伴未改未测，缩放仅 Game View。
- probe-restore.txt、probe-status.txt、final-verification.txt 恢复环境、起始场景和 Game View，CharacterHome 停止且不脏，探针清理，14:30:53 UTC 最终编译无失败，diff 通过，四条 Unity 生成空名仅规范化空白不改值。
