# 装备

[English](EQUIPMENT.md) | [简体中文](EQUIPMENT.zh-CN.md)

历史设计来源为 Cat Quest.pdf 的 4.4.8、4.5.4、表 4.6–4.8、4.5.5 和 4.6.4，PDF 页 58、62–67、75。PDF 不随仓库分发。

## 规则与保存

- 三个确定性 Boss 套装，各有武器 ATK、护甲 DEF、头饰 ASPD。
- 获得时等级一，设套装阶 T、装备等级 E，武器护甲加成为 `2T + 0.5(E - 1)`，头饰为 `0.05T + 0.01(E - 1)`。
- E 升 E+1 消耗 `10+5E` 鱼币，不超过 Cat Level。原子保存，升级请求收据使重试幂等。
- 两件加等级基础最大 HP 的 `5T%`，半数向上取整。三件保留 HP 并再加 T ATK、T DEF、0.02T ASPD，不混算不同套装。
- 最终属性决定实际伤害和攻击间隔，确认战斗快照同时记录最终属性和装备等级。战斗运行时禁止换装。
- 普通 Boss 5/10/15 胜分别解锁固定武器、护甲、头饰。领取不给鱼币，实际领齐后后续胜利使用原有重复奖励。
- Schema 5 保留任务、专注、货币、Boss 与待领进度，增加背包、装备 ID、升级收据和永久解锁。
- 领取符合条件的 Daily Reward 后永久解锁装备，沿用既有独立奖励卡机制，不更改奖励系统。
- Cat Home 展示森林猫咪、三个装备槽、真实最终属性及战力、方格背包，筛选 All、Helmet、Weapon、Equipment。没有容量售卖，Boss Stage 直接打开领取，Cat Home 不领熟练度，Dashboard 装备槽链接物品管理。
- 图标使用已有 GUI Pro 资源，只改变属性，不引入新皮肤或制作强化树。

## 已保存的静态界面

绑定在 `Assets/Prefabs/Canvas.prefab`，旧一次性页生成器已退役，只改指定组件。Cat Home 静态树为 Canvas/Container/Content/CatHome，在 CharacterHome 进入 Play 前已存在。EquipmentSurface 包含 CharacterCard 与嵌套 InventoryCard，前者含标题、预览、装备和 EquipmentStats，后者为 EquipmentInventory。ItemDetail、ItemModal、ConfirmEquipment 是已保存子对象，Boss 有独立装备弹窗。

EquipmentPageUI 绑定控件并填数据，不创建这些静态面板。编辑时可启用 CatHome 并暂时隐藏其他页，但不保存改变后的初始页配置。LiveCat 绑定现有预览相机角色。Settings 也是 Canvas 预放且初始禁用实例，由 SettingsLauncher 打开。

保留原样式和动态背包模板。任务筛选确认及 Focus 退出确认仍有运行时构建路径，本次静态迁移未涉及它们。

## 物品、属性和交互

Cyclope 蓝、Orc 紫、Demon 黄，背包、已穿戴和领取采用 SimpleCasual 圆角底与阶色，不再生成 EquipmentGradeEffect 动画边框。升级预览及战斗武器特效独立保留。

Auto Select 枚举拥有的搭配并包含套装效果，同 Power 保留现搭配，点击直接原子提交最优，不弹确认，仍受解锁和战斗限制。属性行显示真实 HP/ATK/DEF/ASPD 及独立 +gear，零加成时单独的加成标签留空，没有填充条或暗示最大值，速度按每秒显示。

Auto Select 在悬停或键盘选中时说明“Highest Power · includes sets”。成功保存后显示勾选图标；搭配有变化时另显示真实 Power 差值并短暂高亮相关槽位，相同搭配只显示勾选。保存失败保留错误提示，不显示成功反馈。减少动态效果时保留勾选和 Power 差值，停止槽位高亮。空背包使用状态图标、必要的等级／胜场门槛及操作按钮，按当前进度进入 Daily Rewards、Tasks、Boss 或已获得的 Mastery 领取；空筛选提供 All equipment。不显示装饰加号或重复说明段落。悬停／详情使用独立装备快照，不复制任务与 Focus 历史。

物品详情是 `Assets/Prefabs/EquipmentItemDetail.prefab`，源于 SimpleCasual Light/Equipment_Popup_Detail。EquipmentPageUI 提供物品、等级、当前下一加成、套装和鱼币成本，EquipmentItemDetailUI 保留绑定并限制键盘到有效操作。背景关闭且不穿透，卡内点击和拖动不关闭。装备卸装直接执行并反馈，点击鱼币直接升级，失败在详情显示。熟练度宝箱直接领取，另一个未拥有物品详情仍保留 Claim 确认。保留 Fredoka，两处 Canvas EquipmentPageUI 引用共用详情，Boss 选择面板独立。

EquipmentPageUI 复用一个 TaskDialogKeyboard 管理最上层详情、熟练度或确认弹窗。Tab／Shift+Tab 只循环当前可见且可用的操作；Escape 关闭顶层界面（存在悬停预览时先关闭预览）。确认默认选中 Cancel，取消后返回原操作；最后一个弹窗关闭后恢复仍有效的入口焦点。背景关闭或离页会释放键盘控制并清空待确认回调。只调整输入处理，保留现有布局和领取、装备、升级规则。

Dashboard 与 Cat Home 共用 `Assets/Prefabs/EquipmentStats.prefab`，来源为 Light Equipment/Panel_Left/Stats_Group 及项目字体颜色，显示含套装的最终属性与 CombatStats.Power。槽底共用阶色，详情、熟练度和确认遮罩覆盖根 Canvas 阻挡顶栏侧栏，并由页面管理生命周期。升级价使用顶栏鱼币图标。

`Assets/Prefabs/EquipmentInventory.prefab` 适配 Light/Equipment Right_Panel，独立筛选条、六列四行网格、等级徽章和 Auto Select。无售卖、容量、Inventory/Equipment 大标题或蓝色已保存消息。空背包和锁定状态使用现有项目图标及简短操作标签，绿点表示已装备，EquipmentInventoryUI 持有模板和响应网格，EquipmentPageUI 负责数据操作。

两个角色卡的战力使用 Light/Character_Detail_2 的 Middle_Character_Group/Platform/Power 拳头数字徽章，保留原文本对象、字体和真实绑定，用图标替代 Total Power 标签。

鱼币升级按钮悬停或键盘选择时，在当前属性旁以绿色显示下一等级实际差值，并用 EquipmentUpgradeFeedback 展示 Upgrade_Green，独立于 JuicyButton。移出、禁用、确认或失焦清理，预览不消费。点击直接带显示等级成本交服务检查。战力徽章增大，Dashboard 猫预览上移，底部 TaskStatus 成功条已移除，任务错误用既有可关闭弹窗。

Dashboard 槽与战力徽章保持间距，空槽用 512px PictoIcon_Attack/Armor/Helmet 灰化，装备后恢复原彩色图标。详情不再有单独 Next level 卡，当前属性内放悬停差值与特效，鱼币文案仍 Upgrade。套装标签使用蓝紫黄 Label_Tag00，三个进度点与两张阈值卡显示实际 2/3 件套进度，均可编辑序列化。

已穿戴和背包物品悬停可打开只读详情，复用同数据卡并随指针移动、翻转夹在屏幕内，不变暗、不抢射线或键盘。空槽无提示。移出、按下拖动、Escape、来源禁用、刷新、关页或失焦关闭。点击仍打开居中可操作模态框，悬停不改变选中物品或存档。

## 共用装备光环

`Assets/Prefabs/CatCharacter.prefab` 是项目 Ninja 变体，其嵌套 `Assets/Prefabs/CharacterEquipmentAura.prefab` 持有圆环引用、身体锚点、比例和三件抬升。旧场景和训练演员复用同一表现预制体，保留原对象控制器动画。改共用资产即可同步，不改供应商 Ninja/VFX。

只计算有效、拥有且已装备部件，显示件数最多的阶色，同数选更高阶，无装备无环。蓝色一二三件用 Circle_Effect_7/13/19，紫色 9/15/21，黄色 10/16/22。圆形效果在身体后，旧 Magic_Circle 地面效果不再实例化，Demo_Item_Icon_3 仅在运行实例隐藏。仅表现，不改属性奖励存档，随已提交 TaskService.Changed 刷新，禁用清理重开重建。

Dashboard、Cat Home、Focus、三 Boss 场景及现有 Ninja 共用。伙伴只接收主进程选定阶与件数，不读存档，断连隐藏直至有效状态恢复。

环境遮挡通过运行时把猫和光环组成排序组，再在组内把粒子放猫后。卸下禁用自有组恢复原排序。共用比例 3.5，characterOffset `(-0.9, -0.55, 0)`，三件抬升 0。字段用 FormerlySerializedAs 从 footOffset 迁移。粒子初速度覆盖为零防止 Boss 透视深度漂移，纹理动画保留。

## 宝箱领取反馈

EquipmentClaimUI 保留一排固定物品宝箱，不保留下方选择行。蓝紫黄阶色，成功提交后揭示物品和 Owned。锁定和已领也支持不阻挡的实际属性预览。服务胜场和原子结算防止提前重复，旧乱序已拥有物品保留且剩余可领。

成功领取使用 JuicyButtonMesh 在 0.55 秒内由 35% 放至 134% 再回弹正常，ChestRewardParticles 播放 0.95 秒金色光晕、扩圈和更大星爆。参考 Light/Popup_Reward_Get 与 Preview/082_Popup_Reward_Get，保留已编辑弹窗、Fredoka 和阶卡。成功持久化后使用未缩放时间，命中区不动，关闭失焦清理，失败重复和重开 Owned 不重播。

设计参考 [NN/g 有目的动画](https://www.nngroup.com/articles/animation-purpose-ux/)和 [Apple 局部成功反馈](https://developer.apple.com/design/human-interface-guidelines/motion)，短局部反馈配持续 Received/Owned，不使用阻断庆祝屏。

历史版本曾由 `Tools/Generate-EquipmentClaimAudio.py` 生成一秒拾取和弦，0.18 秒解决和弦配图标峰值。**当前 `equipment_claim.wav` 已随 2026-10-31 获批音频替换，来源以 [声音指南](SOUND.zh-CN.md) 和 `AUDIO_MANIFEST.json` 为准，不运行旧生成器覆盖。** 现有 SettingsAudio 槽与 EquipmentClaimUI.PlayCollected 仍在成功提交后同步声音动画，遵循 SFX 音量静音，失败重复和重开保持安静，普通按钮和音乐不变。

## 验证

| 请求 | 结果与范围 |
| --- | --- |
| `Temp/verify-equipment.request` | `Temp/verify-equipment.result`，九物品、所有权槽位、套装公式、等级资金、失败重试重复陈旧、迁移重载及下游聚合 |
| `Temp/verify-equipment-play.request` | `Library/EquipmentVerification/`，两种标准尺寸、确认取消、Stage/Dashboard 入口、属性和 ASPD 动画 |
| `Temp/verify-equipment-hover.request` | 时间戳 `Library/EquipmentHoverVerification/`，移动屏幕角、穿透、关闭、恢复模态、数据不变及装备升级，`before` 拍摄当前点击界面 |
| `Temp/verify-character-aura.request` | 时间戳 `Library/CharacterAuraVerification/`，隔离显示行为，不生成修复资产 |
| `Temp/verify-chest-reward.request` | 隔离领取悬停、固定边界、旧装备补齐、揭示序列、键盘、失败重试重复、暂停和中断清理 |

所有检查使用独立存档，不修改玩家数据。已有报告不是当前版本的验证证明。
