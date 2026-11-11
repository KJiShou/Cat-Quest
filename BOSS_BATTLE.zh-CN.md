# Boss 战斗

[English](BOSS_BATTLE.md) | [简体中文](BOSS_BATTLE.zh-CN.md)

原始参考为 Cat Quest.pdf 第 4.5.4–4.5.5 节，PDF 62–68 页，历史本机路径 `D:/Downloads/Cat Quest.pdf` 不随仓库分发。装备获取、升级、穿脱及属性修正见[装备指南](EQUIPMENT.zh-CN.md)。

## 规则

- 猫咪基础属性由保存经验和等级曲线计算：HP 为 `100 + 10*(level-1)`，ATK/DEF 为 `5 + level-1`，ASPD 为 `1.00`。
- Power 仅显示，公式为 `round(HP + 10*ATK + 8*DEF + 100*ASPD)`。
- 伤害为 `max(1, round(ATK - 0.5*DEF))`，原文未定义 .5，此实现统一远离零取整。
- 首次攻击在一个攻击间隔后，后续每 `1 / ASPD` 秒攻击，同时致死猫咪获胜。120 秒战斗上限不含开场跑入。
- BattleSimulation 使用攻击计数和双精度时间，帧率、动画事件不授予伤害或改变结果。
- 确认入场原子记录不可变属性并扣一券。有效失败和主动 Stop 消耗入场，技术错误、超时及启动发现未结束战斗只退款一次。
- 结算按保存快照重算结果，奖励、熟练度、结果和解锁一起提交，失败重试不重复发奖。
- Cyclope、Orc、Demon 的 HP/ATK/DEF/ASPD 分别是 170/13/8/1.00、340/25/15/1.00、650/40/24/1.10。Orc 需 Cyclope 胜利及 Lv.10，Demon 需 Orc 胜利及 Lv.20。
- 首胜为 30/60/100 EXP，后续普通胜利含装备里程碑为 10/20/30 EXP。这些券消耗型奖励独立于任务和专注额度，已结算收据不重付。5/10/15 胜分别解锁武器、护甲、头饰宝箱，直接点击领取，保留旧已拥有和待领状态。三件实际领齐后才开始重复胜利鱼币奖励。
- 首次 Demon 胜利解锁 Master，三组基础属性及逐轮线性缩放沿原设计。只可进入当前关，胜利奖励 `50+10*n` EXP 与 `25+5*n` 鱼币并推进。
- Schema 5 保留旧任务、专注、货币经验，并保存 Boss 熟练度、待领、战斗快照收据和下一 Master 关。

## 场景与战斗表现

`BossBattle1.unity` 是作者场景来源，战斗使用导出预览预制体。Stage 打开来自 SimpleCasual Light/Stage 的三个六边形头像卡。第二关 `OldForestBossStage.prefab` 来自 `Assets/Scenes/Old forest.unity`，第三关 `LavaDungeonBossStage.prefab` 来自 `Assets/Scenes/Lava dungeon.unity`，保留环境、相机投影和 Boss 位置，相机沿第一关约定水平居中角色。猫根名 `Ninja Cat` 供现有绑定使用。BossArenaPreview 按普通或 Master 所选 Boss 切换，BossBattleActors 复用原 Cyclope/Orc/Demon。

猫先跑到剑程再开战，攻击片段每间隔一轮，速度 `clip.length * ASPD`，Idle/Run/Death 不按 ASPD 加速。红闪和伤害数字不打断攻击节奏，入场重置 HP。离页明确 Stop，关闭应用则恢复未结束入场券。

战斗右上角的 Skip 按钮直接将当前模拟推进至结果。入场仍需确认并消耗一张券。跳过战斗沿用正常胜负结算与保存重试流程，不额外扣券。战斗中离页仍会停止战斗。

`BossPageSetup` 只保留 BossBattle1 场景预览同步，包括 Refresh Boss Scene 和源场景保存触发。旧 Canvas、控制器和头像生成路径已退役，保存资产仍可编辑。原角色、怪物和用户场景不改，只在战斗实例禁用怪物 demo 行为。

## 命中与胜利反馈

实际模拟命中触发 0.32 秒向外后坐、倾斜、压缩回弹，叠加于猫和 Boss 当前姿势。保留 ASPD，视觉冻结后恢复攻击相位，不从反馈计算伤害或模拟时间。重复命中重启有界反应，结束、重置、换关、禁用和失焦清位移与闪光。

每次命中只冻结两个 Animator 55 ms，保持后坐和闪光，相机进行 160 ms 衰减抖动，同时命中共用一次停顿。恢复时补回持有时间，BattleSimulation 与 Time.timeScale 不暂停。清理精确还原速度和相机。

普通已提交胜利为死亡表现预留 1.8 秒：160 ms 最后姿势停顿，0.55 倍死亡片段，6% 相机推进后还原投影，一次衰减抖动，暖色淡至透明。Boss 保持倒下和零 HP 直至关闭结果，再显示现有 Light/Play_Result_Victory_Common 衍生结果、声音及奖励飞行。Skip 立即展示结果。先保存再庆祝，失败仅 Retry Save。离页取消等待结果，禁用重置失焦清表现，死亡期间隐藏 Master 光环，预览重置时恢复。不改战斗公式、奖励、schema 或全局时间。

设计参考为 [Sakurai 的重要时刻停顿](https://www.youtube.com/watch?v=OdVkEOzdCPw)、[八种 Hit Stop 技巧](https://www.youtube.com/watch?v=tycbMSjDDLg)及[打击感研究](https://faculty.washington.edu/zkwen/articles/lin22features.pdf)。具体时长强度为本项目选择，不声称来自论文数值。

Stage 宝箱在 pendingClaims 大于零时显示轻量金星，不拦点击，关面板或领完立即停止。胜利保留 Victory 标题，仅在正值时显示已提交 EXP 和鱼币图标数额，各自零值条隐藏。共用 RewardFlightUI 在成功保存后从数额飞往对应顶栏，失败无飞行，重试重复不能多付。

胜负结果仅保留 Continue 按钮，键盘默认选中该按钮。胜利显示已提交的 EXP／鱼币奖励，额外说明隐藏；战败显示“Train and try again”和“No rewards · Progress kept”。Continue 沿用退出／重置转场并返回 Challenge，不自动入场或领取装备；Master 胜利在该转场中选择已保存的下一关。熟练度进度和领取仍位于关卡选择器，保留 5／10／15 胜固定解锁武器／护甲／头饰。结果页不显示新增引导、熟练度条或次要目的地按钮。

## Master 与熟练度

Master 结果打开时保留刚完成关，关闭或离页重开选择已保存下一关和环境，但不自动开战，Challenge 仍需确认一券。失败、Stop、未成功结算不推进。

Master 选择器是 Canvas 的 StageSelection/Master 可编辑金色卡，复用项目边框、Light/Stage StagePIcture 六边形及皇冠、原字体和 JuicyButton。标题、下一 Boss 描述、锁提示和入口文本由 Presenter 绑定，选择只预览当前关。

`Assets/Prefabs/Boss/MasterBossAura.prefab` 为 Cyclope 金橙、Orc 紫、Demon 暗红，固定身体中心锚点、轻未缩放呼吸与稀疏上飘粒子，排序低于角色且无深度速度。普通关隐藏，场景实例管理生命周期，BossArenaPreview 序列化绑定，BossPageUI 传入 Master 状态，不改规则。

EquipmentClaimUI 只保留一排宝箱，蓝紫黄表示三个 Boss 品质。每箱预览固定物品，提交成功后显示 Owned。锁定和已领也可使用不阻挡的真实属性悬停预览。服务胜场门槛和原子结算防早领重复，旧乱序获得装备保留，剩余仍可补齐。

普通胜利成功提交后，猫先原地停留，等待现有 1.8 秒 Boss 倒下表现结束，再从右侧跑离并显示结果；Skip 与战败保留立即离场。Continue 立即播放局部 EndTransition，在遮挡下重置普通/当前关或切换已保存下一 Master，再播放 StartTransition。两个过渡是 Boss Arena 遮罩内现有预制体实例，有序列化绑定，不改全局过渡或顶栏侧栏。返回中忽略重复 Continue/Challenge/Stage，离页取消清理，失败回同关，下一战仍需确认扣券。

## 验证

所有检查使用隔离存档，现有报告仅是历史。装备在 Cat Home，排行榜另行处理。

| 请求 | 覆盖与证据 |
| --- | --- |
| `Temp/verify-battle-rules.request` | 数学、帧率独立、门槛、失败重试、首胜、熟练度、Master、恢复迁移及 Focus/奖励/任务回归 |
| `Temp/verify-battle-play.request` | 门槛头像、确认扣券、跑入攻击胜利、ASPD=2 动画及离页 Stop，两种标准尺寸，`Library/BattleVerification` |
| `Temp/verify-boss-skip.request` | 确认暂停恢复、确定性胜败、重复、失败重试、关闭离页及故事参考，`Library/BossSkipVerification/` |
| `Temp/verify-boss-death.request` | 三个 Boss、死亡运动、延迟结果飞行、重复、失焦离页、失败重试、重载和即时 Skip，时间戳 `Library/BossRewardVerification/` |
| `Temp/verify-boss-reward.request` | EXP/鱼币正零值、飞行顶栏、普通和 Master、失败重试重复、关闭重载、退出与过渡及 Master 映射，`Library/BossRewardVerification/` |
| `Temp/verify-boss-stages.request` | 三场景演员绑定、重复切换、跑动命中、Master 映射、重开及两尺寸，`Library/BossStageVerification/` |
| `Temp/verify-master-card.request` | 锁定解锁、指针键盘反馈及两尺寸，`Library/MasterCardVerification/` |
| `Temp/verify-master-aura.request` | 普通/Master 可见性、颜色锚点、运动排序、战斗重置和生命周期，`Library/MasterAuraVerification/` |
| `Temp/verify-chest-reward.request` | 宝箱 UI、悬停、领取，EquipmentVerification 还覆盖固定门槛、重试和旧装备补齐 |

一次性场景导出助手已删除，不能把验证当生成或修复操作。
