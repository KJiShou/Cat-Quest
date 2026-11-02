# Cat Quest Windows 性能优化报告

2026-11-02。以下编号对应原性能诊断的 15 项。最明显的三个变化是：首页纹理从 **3,256 MiB 降到 619 MiB**，144 条任务时切换 Todo 的最长操作帧从 **317 ms 降到 20 ms**，Focus 运行时的逐帧托管分配中位数从 **122 KiB 降到 0.20 KiB**。

同平台 Development Build 目录实测由 **3,330 MiB 降到 985 MiB**，包含 Unity 运行库与开发构建文件，未使用 ZIP 压缩。

## 如何比较

主对比在同一台 Windows 11、i7-14650HX、RTX 4060 Laptop 上，用 Unity 6000.3.10f1 的 Windows Development Player 完成。主窗口使用 DX12、1920×1080、Very Low 和隔离的 144 条合成任务。另跑了 1280×720、24 条任务，以及独立的 DX11 桌面宠物。测试使用了本次保存后的场景。

表内的“峰值”指操作开始后前 12 帧中最大的帧间隔；“分配”指该窗口内单帧最大的 GC 分配。稳定态剔除前 25 帧。改前数字来自原诊断的同口径路线，另建改前 Player 供画面对照。改后主窗口有 60 FPS 帧率上限，GPU 或主线程的纯执行时间不能直接用 16.67 ms 的帧间隔代替。每组是本机的一次路线采样，改后绝对值有自然波动。

## 逐项改动和结果

| # | 问题 | 实际改动 | Profiler 或功能验收 |
|---|---|---|---|
| 1 | 忍者猫和 Sensei 的独立 RGBA32 动画帧常驻 | 按角色、动作及过滤方式创建项目自有 Sprite Atlas。Windows 使用带透明通道的 BC3/DXT5。保留原图、pivot、GUID 和现有动画片段，运行时使用 Unity 打包映射。 | Dashboard 改前枚举到该包 **515 张、2,058 MiB**；改后实际加载 **30 张对应的压缩图集页、226 MiB**。 |
| 2 | Focus 鱼群每帧复制存档来计算数量 | 鱼群数量按整分钟、日期和已提交数据版本缓存。奖励预览只读当日记录，外部需要修改的副本仍独立。鱼的运动与 Focus 结算各自沿用原逻辑。 | 144 条任务，运行 Focus 时分配中位数 **121.98 → 0.20 KiB/帧**。额外的固定 60 FPS 对照为 **118.45 → 0.20 KiB/帧**。 |
| 3 | 长任务列表和搜索反复创建全部控件 | 可见范围和前后缓冲区内复用原有行、标题与标签；占位高度维持滚动范围。小列表直接展示全部。一次刷新共用任务查询快照，取消重复导航刷新。回收的行保留勾选框和标签的引用，重新绑定操作目标。 | 首次进入 **421 → 74 ms**，Todo **317 → 20 ms**，Tags **325 → 17 ms**。输入仍匹配大量任务的首字符 **206 → 17 ms**。Todo 的单帧分配 **37.1 → 0.90 MiB**。 |
| 4 | Tasks 隐藏时仍响应存档事件 | 离开页面时取消订阅；返回后按当前页签、日期和数据重新显示。保留原有草稿提交和离页逻辑。 | 普通路线打开 Tasks 后再开始 Focus，操作峰值 **223 → 65 ms**；End Early **191 → 44 ms**。这些是整条操作的联合改善，也包含第 2、11 项。 |
| 5 | 未打开宠物仍然轮询文件并写心跳 | 无子进程时主程序跳过通信循环。启动前发布初始状态，连接后继续发布心跳、校验身份和去重；装备光环只在数据版本变化时重算。窗口开关只在状态变化时刷新显示。 | 144 条任务、60 FPS、宠物关闭的 Dashboard，对照操作窗口峰值 **41 → 17 ms**。独立 Player 的宠物由真实主进程启动。已连接时的文件 I/O 仍然存在。 |
| 6 | 未访问的场景、训练背景和 Acorn 帧占内存 | 背景依照场景包、过滤方式和 mipmap 分组打包。三个 Boss 场景和 Focus 各使用独立资源入口，保留原 prefab 及其 GUID。Focus 按需异步加载；加载期间离页会解除回调，计时不依赖美术。 | 改前 Dashboard 场景包约 **895 MiB**，改后对应的已加载环境图集页约 **210 MiB**。开场的 Acorn 图集为 **0 页**。首次打开 Focus 的同步处理 **26.5 → 8.6 ms**；改后稳定显示 Focus 时纹理约 **740 MiB**。 |
| 7 | 返回 Dashboard 重建全部任务预览 | Dashboard 使用同一可视行机制，保留原有分组、全部任务和点击入口，每次更新共用一份查询快照。 | 返回 Dashboard **308 → 17 ms**；单帧分配 **32.2 → 0.83 MiB**。 |
| 8 | 选日期也会重查整月并重建抽屉 | 月数据按月份、日期、数据版本、搜索和过滤条件失效。仅切换当天选中态时保留月投影。日期抽屉同样按可视范围复用行。 | 打开日期抽屉 **92 → 36 ms**，分配 **10.3 → 3.3 MiB**。首次进入月历仍会计算并绑定 42 天。 |
| 9 | 主窗口静置仍以数百 FPS 渲染 | Windows 主窗口前台目标 60 FPS、失焦 30 FPS，独立宠物沿用 30 FPS。Focus 仍用单调时钟计时。 | Dashboard 稳定态中位帧间隔约 **16.67 ms**；420×300 宠物约 **33.33 ms**。没有测量实际瓦数或续航。 |
| 10 | 场景预览纹理按固定高分辨率分配 | 根据 RawImage 的显示尺寸及 Canvas 比例申请 RT，保留画幅与尺寸上限。Focus 离页释放 RT，Boss 延用离页释放，角色预览保留复用。 | Dashboard 的 Render Textures **72.5 → 48.6 MiB**，Boss 页面 **134.9 → 111.5 MiB**，宠物 **12.4 → 3.0 MiB**。这些是各进程的总 RT 计数。 |
| 11 | 余额、标签、奖励日或回执也读取整份 PlayerData | 给常用标量、标签、奖励日、Focus/Boss 回执提供窄查询。任务页多次投影共享同一份读取快照。保留提交端的复制、原子替换、备份和失败重试。新增回归覆盖旧奖励日懒读取、外部修改副本、失败写入、成功替换和跨日。 | 完成任务 **239 → 63 ms**，单帧分配 **25.3 → 3.0 MiB**。同步保存和部分较大查询仍有成本。 |
| 12 | 聊天追加一条回复会重建完整历史 | 追加消息只创建新气泡；草稿内容或状态改变时只重绑对应卡。首次恢复长历史按每批最多 8 条逐步生成。清空和替换会话时清理旧行，保留原来的草稿确认与权限逻辑。 | 本地 80 条首显峰值 **71 → 17 ms**；追加第 81 条 **71 → 17 ms**，该操作单帧分配 **7.4 MiB → 55 KiB**。首显峰值降低来自分批，完整历史需跨帧显示。 |
| 13 | Boss 入场、结算和装备刷新夹带无关工作 | Boss 页面只读所需的阶段、精通和本次战斗回执。背包按装备 ID 复用控件与空格，保留绑定和原有升级反馈。共用第 3、4、11 项的改善。 | Boss 1 入场 **166 → 35 ms**，Boss 3 入场 **203 → 57 ms**。装备详情与升级组合操作 **78 → 51 ms**。首次选未载入的 Boss 场景仍有资源载入成本。 |
| 14 | 排行榜首次创建 50 行集中在一帧 | 首次先填最多 8 行，后续每帧最多新填 6 行；切分类继续复用已有行，新的结果替换旧的待填数据。 | 本地 50 行首开峰值 **45 → 21 ms**，峰值分配 **3.1 → 0.77 MiB**。测试未使用真实排行榜后端。 |
| 15 | 奖励数字及静态界面重复格式化、绘制批次较多 | 余额和经验只在显示值变化时格式化，保留飞币期间与共享标签的同步。图集和列表可视化减少实测批次。 | Dashboard Draw Calls **190 → 160**，Boss 静置 **248 → 223**；Dashboard 稳定态分配中位数 **0.393 → 0.203 KiB/帧**。 |

## 证据与复查入口

- [本次 Windows 构建结果](../Library/PerformanceFix-20261102/verified-build.txt) · [144 条任务逐场景汇总](../Library/PerformanceFix-20261102/verified144/summary.csv) · [24 条任务、720p 汇总](../Library/PerformanceFix-20261102/verified720/summary.csv)
- [大列表搜索、聊天和三只 Boss](../Library/PerformanceFix-20261102/verifiedMore/summary.csv) · [训练、装备、Master 和 50 行排行榜](../Library/PerformanceFix-20261102/verifiedExtra/summary.csv) · [独立宠物](../Library/PerformanceFix-20261102/verified144/widget-summary.csv) · [首页纹理对象](../Library/PerformanceFix-20261102/verified144/resources-home.tsv)
- [原始诊断](../Library/PerformanceAudit-20261102-2125/report.zh-CN.md) · [图集与资源绑定检查](../Library/PerformanceFix-20261102/assets-check.txt)。原始采样、截图和测试存档属于本机 Library 证据，不随 Git 分发。

内存单位为 MiB。Texture Memory 包含图形资源估算，与 Gfx Used、Render Textures 等计数有重叠。网络响应、低配显卡、长时间泄漏、所有语言/字体组合和笔电续航不在本次覆盖范围内。

## 可定位的实现

| 编号 | 代码或资产 |
|---|---|
| 1、6 | [项目图集](../Assets/Art/PerformanceAtlases)、[四个场景资源入口](../Assets/Resources/Stages)、[Focus 训练场](../Assets/Scripts/UI/FocusScenePreview.cs)、[Boss 场景](../Assets/Scripts/UI/BossArenaPreview.cs) |
| 2、11 | [奖励读取和旧记录兼容](../Assets/Scripts/Tasks/TaskService.Rewards.cs)、[Focus 观众数量缓存](../Assets/Scripts/Tasks/FocusController.cs)、[共享任务快照查询](../Assets/Scripts/Tasks/TaskService.cs) |
| 3、4、7、8 | [任务列表可视区域](../Assets/Scripts/UI/TaskListViewport.cs)、[任务页生命周期](../Assets/Scripts/UI/TaskSystemUI.cs)、[Dashboard 任务卡](../Assets/Scripts/DashboardTaskUI.cs)、[月历与抽屉](../Assets/Scripts/UI/TaskCalendarUI.cs) |
| 5、9、10 | [宠物通信](../Assets/Scripts/UI/CompanionHost.cs)、[前后台帧率](../Assets/Scripts/GameDataManager.cs)、[预览像素尺寸](../Assets/Scripts/UI/PreviewPixels.cs) |
| 12–15 | [聊天气泡和草稿](../Assets/Scripts/AI/AiChatPage.cs)、[背包](../Assets/Scripts/UI/EquipmentPageUI.cs)、[排行榜](../Assets/Scripts/Leaderboard/LeaderboardPanelUI.cs)、[奖励文字](../Assets/Scripts/UI/RewardFlightUI.cs) |

## 功能与画面验收

- 任务与月历的 Play Mode 验证覆盖 1920×1080、1280×720、1024×768、2560×1080 和侧栏的两种状态。新增 100 条任务用例检查可视行数量、滚动到最后一条后的点击、过滤以及教程定位。排行榜的四分类、取消旧请求、空状态、错误状态和按钮交互通过。[最终任务页回归](../Library/PerformanceFix-20261102/check-tabs-final.txt) · [月历](../Library/PerformanceFix-20261102/check-calendar.txt) · [排行榜](../Library/PerformanceFix-20261102/check-ranking.txt)
- 对照相同工作区的改前 Windows 构建，查看了 Dashboard、任务、月历、Focus、Boss、装备、聊天草稿和排行榜画面。另在 Play Mode 渲染 SimpleCasual 的 Mission、Ranking 和浅色聊天框，检查了训练动画的连续帧。[1080p 前后](../Library/PerformanceFix-20261102/verified144-compare-0.jpg) · [720p 前后](../Library/PerformanceFix-20261102/verified720-compare-4.jpg) · [原厂参考](../Library/PerformanceFix-20261102/vendor-comparison.jpg) · [动画连续帧](../Library/PerformanceFix-20261102/training-motion.jpg)
- 装备、Boss、Focus、奖励、任务和 CharacterHome 的聚合验证通过。独立 Win32 进程通过计时、上限、重试、重复与过期命令、错误 owner、断开后冻结、缩放、最小化、关闭和键盘输入检查。宠物窗口从主程序启动，使用了隔离存档。[聚合检查](../Library/PerformanceFix-20261102/check-domain.txt) · [Focus 原生窗口](../Library/FocusModesVerification/native-20261102-151502/result.json) · [宠物输入](../Library/CompanionInputVerification/native-20261102-151215/result.json)
- 验证期间修正了两个旧预期。原生脚本现在从实际解锁 Focus 的 Lv.2 开始，严格核对 180 分钟单次、240 分钟每日和 3 张票的边界。装备界面沿用项目已有的零加成留空设计，验证器核对此文本，并在隔离测试中跳过会遮住背包的首次启动教程。

## Windows 退出稳定性

DX12 主程序完成 41 个采样状态并写出数据后，有一次在退出时返回 0xC0000005。Windows 事件把故障模块记录为 D3D12Core.dll；原诊断也曾录到该模块的退出异常。本次其余多轮 DX12 Player 正常退出。现有证据定位到原生模块，无法确定 Unity、驱动或游戏资源的具体触发点。

11 月 2 日在发生过异常的同一构建上强制使用 DX11，完整的 144 条任务路线（含真实桌面宠物）和两次剧情退出均返回 0。[Windows 故障事件](../Library/PerformanceFix-20261102/windows-crashes.txt) · [DX11 完整路线](../Library/PerformanceFix-20261102/dx11Route/exit.txt) · [剧情退出一](../Library/PerformanceFix-20261102/dx11Exit2/exit.txt) · [剧情退出二](../Library/PerformanceFix-20261102/dx11Exit3/exit.txt)

11 月 3 日根据上述对照和项目所有者确认，已关闭 Windows Player 的自动图形 API 选择，顺序设为 Direct3D 11、Direct3D 12。正常启动使用 DX11，桌面宠物继续独立使用 DX11。在支持 DX12 的机器上，可从新构建目录运行以下命令选择 DX12。[Unity 配置读回](../Library/WindowsGraphics-20261103-0614/settings.txt) · [Windows Development Build 结果](../Library/WindowsGraphics-20261103-0614/build.txt)

```powershell
.\CatQuest.exe -force-d3d12
```

新构建在同一台 Windows 11 机器上使用隔离存档启动四次。三次默认启动的 Unity 设备日志均为 Direct3D 11，另一次显式指定 DX12，日志为 Direct3D 12。四次均完成 Focus 开始与结算，主进程退出码均为 0。第一次默认启动还打开了真实桌面宠物，关闭后主程序继续计时，宠物进程退出码为 0。上表的性能数字仍来自 11 月 2 日的 DX12 主窗口采样，本轮只验证后端选择与退出。[默认启动与宠物](../Library/WindowsGraphics-20261103-0614/confirmed-default-1/result.json) · [默认启动二](../Library/WindowsGraphics-20261103-0614/confirmed-default-2/result.json) · [默认启动三](../Library/WindowsGraphics-20261103-0614/confirmed-default-3/result.json) · [显式 DX12](../Library/WindowsGraphics-20261103-0614/confirmed-dx12/result.json)

## 另一个未通过的布局检查

背包 Play Mode 验证完成了领取、穿脱、九件装备筛选、升级、Auto Select、余额、重复操作和持久化检查。后续的完整布局断言停在 1920×1080、收起侧栏的 Dashboard。满装备状态下，三个装备格的渲染网格下缘与 Power 徽章边界的垂直间距均为 **−1.86 px**，检查要求大于 **4 px**。用相同的隔离满配存档分别启动改前和改后 Player，测得的控件坐标、渲染间距和数据文件 SHA-256 完全相同。这项布局差异保留给单独的 UI 改动；未将装备整套 Play Mode 验证计为通过。[未通过的断言](../Library/PerformanceFix-20261102/check-inventory.txt) · [改前实测](../Library/PerformanceFix-20261102/fullgear-before/geometry.tsv) · [改后实测](../Library/PerformanceFix-20261102/fullgear-after/geometry.tsv)

性能采样使用带临时探针的开发构建。交付版本去掉了探针，显式持久化了一组已经以 DXT5 运行的导入设置，并清理了任务标签计数与旧的输入标记。前后数字取自带探针且操作记录完整的轮次。临时生成器、诊断脚本及其 .meta 已从 Assets 移除。[交付二进制核对](../Library/PerformanceFix-20261102/delivered-binary.txt)

工作区检查中，本次修改的路径通过 `git diff --check`。全项目另有 5 行尾随空格，均位于本次动手前由项目所有者保存的 CharacterHome 场景差异中；保留了该场景的原始改动。
