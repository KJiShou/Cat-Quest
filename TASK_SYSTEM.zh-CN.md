# 任务系统与效率奖励

[English](TASK_SYSTEM.md) | [简体中文](TASK_SYSTEM.zh-CN.md)

打开 `Assets/Scenes/CharacterHome.unity` 可直接进入主界面，教学启动流程使用 `CatQuestBootstrap.unity`。保留已有名字和进度。侧栏包含 Dashboard、Tasks、Chat、Focus、Boss、Cat Home 和 Leaderboard，后续功能已实现，不再是占位页面。等级门槛见[教学指南](TUTORIAL_SYSTEM.zh-CN.md)。

Todo 按 Due（逾期）、Daily、Today、Tomorrow、This Week、Next Week、This Month、Next Month、Later、No Date 分组，只显示非空组。周一至周日算一周，优先归入更具体的范围，不重复显示；搜索和筛选先于分组。未来开始的每日模板仍受原有完成限制。Todo 数字包含未完成每日任务，服务层的一次性 Todo 查询契约不变。

Dashboard 保留 Daily Tasks 和 Today's Tasks（含逾期一次性任务），其他未来日期、Later 和 No Date 分组仅在有任务时出现。点击一次性任务会在 Todo 中选中它。完成、奖励和每日重置规则不变。

## 页面与交互

| 页面 | 内容 |
| --- | --- |
| Today | 今天及逾期未完成的已安排 Todo，以及今天有效的未完成每日任务 |
| Daily Tasks | 每日模板与当天状态，未来开始的模板不能提前完成 |
| Todo | 未完成的一次性任务和每日任务，按日期分组 |
| Tags | 按标签分组，多标签任务出现在多个组，无标签任务显示在 No Tags |
| History | 按完成时间倒序的记录，详情只读 |

侧栏保留 `Button_Nav` 的紫底白字选中态，鼠标移开或焦点变化不会清除。切页保存非空快速输入并丢弃未提交编辑草稿，返回 Tasks 刷新日期；List 打开设置中的默认页（Today、Daily Tasks 或 Todo），已启用的 Calendar 保留当前视图，具体任务链接仍跳转到对应任务。

未选任务时列表全宽，点击行显示详情，Edit 打开独立草稿。切换页签、筛选移除选择或完成后任务离开列表时恢复全宽。点击行和详情以外区域可关闭普通详情，不影响编辑草稿或已打开弹窗。描述按宽度换行，空描述不占空白。

**+** 打开快速标题输入，Enter 保存非空内容后可继续添加。点击别处保存非空输入并收起，空白不创建，Escape 取消。编辑仅由 **Save Changes** 提交，Cancel/关闭丢弃草稿，空白子任务行不保存。

Today 默认今天，Todo 默认无日程，Daily Tasks 默认每日重复且需要开始日期。日程仅选日期，历史仍显示完成日期和时间。旧 `minuteOfDay` 编辑时保留，但不再参与验证、排序或逾期判断，不需要重置存档。

子任务勾选立即保存。存在未完成子任务时，完成任务需要确认，再一起完成。行勾选框不会同时选中行。历史及今日已完成每日任务可在符合条件时直接撤销，旧每日记录和已删除任务不可恢复。撤销保留子任务及已发奖励，重新完成更新原历史记录但不重复发奖。

Todo 在安排日次日本地午夜开始逾期并显示红色。每日任务按当天执行，午夜未完成记录变为 Missed 且无奖励。同日按标题排序。每日状态在首次访问、午夜、返回或重启时刷新，不补造离线每天的记录。编辑模板影响当前未完成和未来执行，不重写历史。

删除需确认，删除每日模板停止未来重复但保留历史与奖励。删标签仅解除当前关联，管理入口在 Settings。搜索匹配标题、描述、标签，难度和多标签筛选叠加。Today/Daily/Todo 数字表示完整集合而非筛选结果，Tags/History 无数字徽章。被筛选排除的新保存任务仍已保存，只清除选择。

## 数据所有权与持久化

`GameDataManager` 持有共享存档和 `TaskService`，保留 SetUsername/GetUsername/HasUsername/SaveData 接口。`TaskSystemUI` 绑定列表、详情、页签、HUD、搜索与弹窗，`EditTaskDetailsUI` 仅持有编辑草稿。

- `QuestTask` 定义任务，任务、标签和子任务使用稳定 GUID。
- `TaskOccurrence` 保存子任务进度、完成和发奖状态，普通任务日期为 `once`，每日任务为本地 `yyyy-MM-dd`。
- `TaskCompletion` 保存完成时文本、标签、子任务、难度与实际奖励快照，`revoked` 标记撤销，旧数据默认 false。重新完成更新快照但保留原支付金额。
- `TaskService` 负责 Upsert、Delete、SetSubtask、Complete、UndoCompletion、CanUndoCompletion、IsOverdue、SaveTag、DeleteTag、Query 和 RefreshDate。撤销检查历史 ID、任务存在性和日期。变更复制、验证、保存成功后才发出 Changed，失败不替换已提交内存。

存档为 `Application.persistentDataPath/catquest-data.json`。当前 Windows 路径为 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Cat Quest\catquest-data.json`。

保存先写 `.tmp`，原子替换并维护 `.bak`。主文件不可读时尝试有效备份并提示，恢复保存时将损坏原件保留为 `.corrupt.<GUID>`，不以损坏内容覆盖有效备份。名字、任务和成长共用本地存档。当前 schema 5 兼容早期仅名字、任务和 Focus 的版本。线上汇总不等于云存档恢复。

## 奖励与每日目标

Lv.1 的 Easy/Medium/Hard 基础奖励为 **15/25/40 EXP** 与 **6/10/16 鱼币**。存档 Normal 枚举兼容保留，界面显示 Medium。普通任务一次，每日任务每天一次。任务每日基础额度为 **150 EXP / 60 鱼币**，金额和上限使用结算前等级的 `1 + 0.1 × (level - 1)` 倍率，分别向下取整并按剩余额度裁剪。达到上限仍记录完成。当天首个有效首次结算任务自动获得 1 券，独立于货币上限，撤销或重做不重复发。新存档余额为零，不导入场景示例余额。

Lv.30 的 Medium 为 97 EXP/39 鱼币，任务上限为 585 EXP/234 鱼币。预览共用结算公式，包含撤销收据在内的已支付奖励继续占用额度，撤销不能释放额度。

Dashboard 显示真实姓名、等级、等级内经验、装备及权威属性和战力。LV 与绿色经验条来源于 `Lobby_Popup_User_Info`。等级从 1 开始，每级需要 `100 + 25 × (current level - 1)` EXP，允许一次奖励跨多级。

两张 Daily Mission Rewards 卡复用 Mission 外观及 Claim/In Progress/Claimed 状态：

| 目标 | 基础奖励 |
| --- | --- |
| 完成首个任务 | 10 EXP + 5 鱼币 |
| 领取首阶段后累计 3 个不同执行记录 | 额外 20 EXP + 10 鱼币 |
| 累计 25 分钟有效专注 | 10 EXP + 5 鱼币 |

每阶段每日领取一次，按领取等级缩放取整，不占基础额度。普通和每日任务都算，子任务不单独算。撤销减少未领取进度，保留已领奖励。午夜重置且不结转未领取奖励。End Early 秒数计入专注目标，显示完整分钟并兼容旧分钟记录。首任务自动券不需点 Claim。

## 专注结算

番茄钟专注与休息各 1–60 分钟，默认 25/5。正计时可超过三小时，倒计时为 1–180 分钟。只有番茄钟成功结算后进入休息。当前番茄钟 UI 提供确认取消，正计时和倒计时使用 End Focus，确认后结算有效时间，底层与历史提前结束规则继续兼容。

基础货币为每完整分钟 0.5 EXP、每完整 5 分钟 1 鱼币，先按会话开始等级缩放再取整。每次前 180 分钟、每日跨模式前 240 分钟获得货币。每完整 25 分钟一券，专注每日独立最多 3 券。短会话不拼接奖励区间，货币用完后有效 25 分钟仍可满足每日目标。

持久主控制器在切页和切应用后继续计时。休眠、暂停或超过 10 秒的不可验证更新间隔使会话失效，重启不奖励未完成会话。取消、无效、中断无奖励也不开始休息。保存失败冻结结算并等待 Retry Save，不能重复发奖。休息无奖励且可提前结束，跨午夜会话归结算日。详见[专注与伙伴](FOCUS_COMPANION.zh-CN.md)。

## 兼容与升级赠礼

2026-10-31 的奖励调整仅增加字段，schema 保持 5。旧余额、历史收据和已付日合计保留，即使超过新上限。已使用的旧任务日不补发新增首任务券或目标，下个本地日可参与。旧 Focus 目标保留原收据，单纯加载不重写存档。

每个新达到等级 L 赠 `5 × L` 鱼币，逢五级再赠一券，多级合计，与触发奖励原子提交且不占日额度。加载、失败保存、重复结算和撤销重做不会补发或重发。

`Assets/Prefabs/LevelUpCelebration.prefab` 内嵌 SimpleCasual Light/LevelUp，保留 HUD 字体、图标和 JuicyButton。已提交奖励反馈结束后，以不受时间缩放影响的淡入、徽章弹动和光芒显示等级及总赠礼。Continue/Escape 只关闭表现，连续通知排队，目标标签保留 Canvas 序列化绑定。

## 顶栏额度预览

悬停鱼币、入场券或头像分别显示对应额度。鱼币和 EXP 显示任务与 Focus 条，券显示任务和 Focus 已发合计/4，并注明 1 + 3 来源上限。任务按等级上限，Focus 按已奖励分钟和当前会话奖励等级公式，目标与升级赠礼另计。

只读数据来自 `TaskService.TodayRewards`，随服务变化刷新，移开、按下、换页、禁用或失焦时关闭。`Assets/Prefabs/RewardLimitTooltip.prefab` 复用 EquipmentItemDetail 背景、阴影、TopFrame 及顶栏字体，滑条来自 UserInfo/Slider_Green 和 SimpleCasual SliderGroup_UserInfo_Light。无导航、回调或射线拦截，不参与父布局。顶栏目标保留序列化绑定，不重建页面。

## 可选 AI

Chat 可查询任务并提议创建或编辑，均需确认。难度手动，新任务默认 Medium，编辑保留原值。`TaskService.ConfirmChatDraft` 复用原子 upsert，拒绝陈旧、删除、已结算、重复和跨日建议，只分享请求查询的任务事实。详见 [AI](AI_IMPLEMENTATION.zh-CN.md)。

## 验证

在已打开的 Editor 用隔离存档并恢复环境。下列是可用入口，不是本次文档更新的测试结果。

| 入口 | 覆盖与输出 |
| --- | --- |
| Tools → Cat Quest → Verify Task System | CRUD、草稿、筛选标签、去重、撤销历史、日期午夜、兼容与备份回滚，`Temp/task-system.verification` |
| Tools → Cat Quest → Verify MainMenu Play Flow | 保留的历史菜单名，实际任务 UI 与保存流程，隔离文件 `Library/TaskSystemPlayVerification/<GUID>/save.json`，结果 `Temp/task-system.play-result` |
| Tools → Cat Quest → Verify Rewards and Focus | 奖励、额度目标、撤销午夜、结算重试取消休息、迁移等级及任务/CharacterHome，`Temp/reward-system.verification` |
| Tools → Cat Quest → Verify Dashboard Rewards Play Flow | 领取、导航、Focus、HUD、休息取消重载及两种标准尺寸，`Temp/dashboard-reward-play.verification` 和 `Library/DashboardRewardPlayVerification/<GUID>/` |
| `Temp/verify-reward-balance.request` | 隔离平衡、LevelProgression 及 Equipment → Battle → Focus → Rewards → Tasks/CharacterHome，`Temp/verify-reward-balance.result` |
| `Temp/verify-reward-tooltip.request` 内容为 `after` | 两种标准尺寸的额度浮层检查，`Library/RewardLimitTooltipVerification/`，`before` 拍摄关闭状态 |

保留 Canvas 可编辑组件，不运行整页生成。缺失、过时或失败结果不算通过。


## 日历视图

Tasks 顶部提供 List / Calendar 切换。List 保留原五个分类及默认分类设置；Calendar 使用独立的搜索、难度和标签筛选，默认开启 Show completed。当前视图、月份与选中日期只在本次运行中保留，不增加存档字段。

月历以周一开始，固定六周。Todo 按安排日期显示，逾期不会移到今天；已完成 Todo 使用历史快照的安排日期，实际完成时间在详情显示。No Date 展示没有安排日期的 Todo。点击日期打开当日抽屉，点击标题进入详情，新建任务预填选中日期。编辑、完成、确认、删除和合法撤销均走原任务服务；保存失败保留草稿。

今天的 Daily 可以正常操作；未来 Daily 是根据当前模板和起始日期计算的只读预览，不能提前完成或勾选子任务。“Edit daily task”编辑模板，并保留今天的子任务进度。过去只展示保存过的完成快照与 Missed 汇总，不推算离线空白日期。旧 occurrence 没有历史标题／标签快照，启用筛选时不展示无法可靠归属的 Missed 数量。浏览日历不会写入存档或创建未来 occurrence。

宽窗口中抽屉与月历并排；Canvas 空间或实际显示宽度不足时采用覆盖抽屉，避免 1280×720 缩放后编辑区域过窄。抽屉内部可以滚动，日期格不滚动。方向键移动日期焦点，Enter 打开，Page Up/Down 切月，Escape 逐层返回；编辑期间阻止底层月份和视图切换。

可编辑 prefab 位于 `Assets/Prefabs/Task/Calendar`。视图切换沿用 Tasks/TabMenuFrame 风格，按钮复用 SimpleCasual Button_Round01，日期选择器、任务行、详情和编辑器复用项目组件。不要重新生成 Tasks 页面。

日历查询回归加入 Verify Task System 及原聚合链。写入 `Temp/verify-task-calendar.request` 可执行隔离存档的 Play 检查，结果和截图位于 `Library/TaskCalendarVerification/`；验证不会安装或修复 UI 资产。

## 日历按钮表现

今天使用蓝色日期标记，与选中日期底色分别表示。List / Calendar 选中项用藏蓝底白字，另一项用浅底深蓝字。Today 和 Add Task 使用蓝色主按钮，Filter 用天蓝，No Date 用浅青，Clear 用蓝灰。紧凑切片按钮使用 multiplier 2 并减薄阴影，详见[设计上下文](.impeccable.zh-CN.md#紧凑按钮外观)。

月历格中的未来重复次数标为 `Daily · N`，打开后说明其预览性质。Preview 表示未来 Daily 的执行预览，不是已保存的执行记录，也不能提前完成。
