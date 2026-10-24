# Cat Quest 任务系统

MainMenu 的任务 UI 现在由真实任务数据驱动。运行 MainMenu 即可使用，无需先进入 IntroStory；已有用户名存档会保留。

## 使用方式

左侧导航可切换 Dashboard、TaskManager、Focus、Boss、Cat Home 和 Leaderboard。前两项使用原有页面；后四项为预留页面容器。切换页面时提交非空快速输入并丢弃尚未保存的编辑草稿，返回 TaskManager 时刷新日期和任务。右上角显示鱼币和 Ticket；旧存档缺少 Ticket 时从 0 开始，不转换已有 XP。
左侧当前页面保持原有 `Button_Nav` 的 Selected 动画（紫色背景、白色文字）。鼠标移开或输入焦点转移到搜索等控件时，选中样式仍保持；不额外显示蓝色指示线。

- **Today**：今天和逾期未完成的普通任务，以及今天未完成的每日任务。
- **Daily Tasks**：每日模板及今天的完成状态；到设备本地午夜会切换当天状态。未来开始的每日任务不能提前完成。
- **Todo**：所有未完成的普通任务，包括没有 Schedule 的任务。
- **Tags**：按照标签分组显示未完成任务；多标签任务出现在对应的每个分组，没有标签的任务放在 No Tags。管理标签入口暂不放在此页。
- **History**：已完成记录，按完成时间倒序；详情只读。仍存在的普通任务和今天的每日任务可通过左侧勾选框撤销完成；往日每日记录和已删除任务的勾选框禁用。

未选择任务时仅显示全宽任务列表；点击任务行显示右侧详情，点击 **Edit** 打开编辑草稿。切换 Tab、筛选移除选择或完成后任务离开列表时，恢复全宽列表。
点击详情及任务行以外的区域可关闭详情并恢复全宽列表；详情内操作不会关闭面板，正在编辑的草稿和已打开的弹窗不受外部关闭影响。描述按实际可用宽度换行并同步实际区域高度，空描述不显示空白区域。
点击 **+** 在左边直接输入标题，同时隐藏加号；按 Enter 保存非空标题后继续输入下一项。点击其他位置时保存非空标题并收起输入行、恢复加号，空白输入不建立任务，Escape 取消。
编辑中的 **Save Changes** 才会提交；**Cancel / ×** 丢弃编辑草稿。空白子任务行不保存。

创建任务时，Today 默认安排今天，Todo 默认没有安排，Daily Tasks 默认每日重复。Repeat daily 会要求 Schedule。Schedule 只选择日期，不提供时间选择；列表、快速输入和详情仅显示安排日期。History 仍显示实际完成日期与时间。旧存档的 minuteOfDay 字段保留兼容，编辑时保留原值，但不再参与日程校验、排序或逾期判断，无需迁移或重置存档。

详情页勾选子任务立即保存。Complete 会在还有未完成子任务时要求确认，然后一起完成并领取奖励。
任务行左侧勾选框可直接完成任务，沿用未完成子任务确认；该点击不会选择任务行。History 和 Daily 当天已完成任务的勾选框可撤销完成，不弹确认。撤销保留子任务进度和已获奖励，同一次任务重新完成恢复原历史记录、更新时间，不重复发奖。未完成普通任务在安排日期的次日本地午夜起视为逾期，日期在列表和详情显示红色；安排当天不逾期。每日任务以当天为执行日期，当天不逾期，午夜后继续沿用每日重置规则。相同日期的任务按标题排序，不再使用旧时间。
Easy / Medium / Hard 分别获得 **10 / 20 / 30 EXP 和 5 / 10 / 15 鱼币**；存档内部保留 Normal 的枚举编号，界面统一显示 Medium。普通任务一次，每日任务每个日期一次。任务每天最多发放 120 EXP 和 60 鱼币，分别按剩余额度裁剪；达到上限仍记录任务完成。新存档余额从 0 开始，场景里的示例任务和 500 不导入数据。

删除任务先确认；删除每日模板停止后续重复，历史和奖励仍保留。删除标签仅解除当前任务的标签关联，历史快照不变。

搜索匹配标题、描述和标签；Filter 可叠加难度和多个标签。Today、Daily Tasks、Todo 的数字表示该 Tab 的完整集合；Tags 和 History 不显示数字徽章，搜索筛选只影响当前列表。当前筛选排除刚保存的任务时，选择状态清空；任务仍能从对应 Tab 找到。

## 数据如何串联

`GameDataManager` 管理唯一存档和 `TaskService`，保留 SetUsername / GetUsername / HasUsername / SaveData 接口。

`PlayerData` 增加版本、鱼币、Ticket、总 XP、任务、标签、执行进度和完成历史：

- `QuestTask` 是任务定义，任务、标签和子任务均使用 GUID。
- `TaskOccurrence` 是实际执行状态：普通任务的 day 为 once；每日任务的 day 为本地 yyyy-MM-dd。记录子任务进度、完成状态和奖励是否已发放。
- `TaskCompletion` 保存完成时的标题、描述、标签名称、子任务、难度和奖励快照，编辑模板不会重写历史。`revoked` 标记撤销记录，旧存档缺少该字段时默认 false；重新完成会更新该记录的完成快照，原奖励金额保留。

`TaskService` 是统一业务入口：Upsert、Delete、SetSubtask、Complete、UndoCompletion、CanUndoCompletion、IsOverdue、SaveTag、DeleteTag、Query 和 RefreshDate。撤销使用 History 记录 ID，并校验任务仍存在及执行日期可恢复。变更先复制数据、验证和存档，成功后发出 Changed；写入失败不替换内存数据。

`TaskSystemUI` 绑定 MainMenu 的列表、详情、Tabs、HUD、搜索和对话框。`EditTaskDetailsUI` 只管理独立草稿。标签选项从存档目录读取，按照稳定 ID 保存；多选勾选和标签 × 删除同步。

每日状态在当天首次访问时创建。午夜、返回游戏或重新启动时刷新，不补生成离线期间的日期。每日模板修改影响当前未完成项目及后续日期，已完成历史保留。

## 存档位置与恢复

文件：`Application.persistentDataPath/catquest-data.json`。

Windows 当前项目默认路径为：
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Cat Quest\catquest-data.json`

- 旧的仅含 username 的 JSON 自动扩展为 version 2。
- 保存先写 `.tmp`，再原子替换主文件，上一份有效数据保存为 `.bak`。
- 主文件无法读取时尝试备份，并显示恢复提示。
- 损坏的主文件在恢复保存时另存为 `.corrupt.<GUID>`；有效备份不被损坏内容覆盖。
- 玩家用户名、任务进度、完成记录和奖励在同一份数据中保存。

不包含云同步、通知提醒或装备系统。右上角第二种资源为 Ticket，不显示 XP，任务完成不会增加 Ticket。

## Dashboard 与奖励 / Focus

Dashboard 默认打开；中间沿用 Ninja Cat 角色和现有预览，标题显示玩家名字，显示真实 LV、等级内 EXP 和进度。等级行复用 `Lobby_Popup_User_Info` 的 LV 数字与绿色经验条。等级从 1 开始，每级需要 `100 + 25 × (当前等级 − 1)` EXP；一次奖励可以连续升级。三个装备槽复用 `Equipment` 的灰色空槽，排列在角色下方，不显示槽位文字；本次不显示虚构 Rank、Power 或已装备物品。

右侧 Daily Mission Rewards 拆成两项，各自进度、Claim 和领取状态：当天完成三个不同任务执行记录，或当天累计有效 Focus 满 25 分钟。提前结束的会话也算，按秒累加，进度显示完整分钟；旧存档按已记录的有效分钟兼容。外观直接复用 `Mission` 的红色飘带、任务条目、带刻度的蓝/绿进度条、绿色 Claim 与灰色 In Progress；已领取显示灰色 Claimed。Daily Task 和普通 Todo 都算，子任务不单独计数。每项奖励 20 EXP + 10 鱼币，每天各领一次；不占任务或 Focus 基础奖励额度。撤销完成减少未领取目标进度，已领取奖励保留；重做不会重复发奖。目标在设备本地午夜重置，不结转未领取奖励。

Focus 页提供番茄钟（专注/休息各 1–60 分钟，默认 25/5）、正计时及倒计时（1–180 分钟）。仅番茄钟在成功结算后进入 Rest；正计时可超过三小时并手动结束。正常结束或主动 End Focus Early 按完整有效分钟结算：每分钟 1 EXP，每完整 5 分钟 2 鱼币，每完整 25 分钟 1 Ticket。完整间隔按单次会话计算，不拼接短会话。每次最多前 180 分钟、三种模式每天共享前 360 分钟可获得 EXP / 鱼币，每天最多 4 张 Ticket。完整 25 分钟仍满足目标，即使基础奖励额度已耗尽。

Focus 计时由持久化管理对象维护，切换页面和切换到其他应用继续计时。设备休眠、暂停或超过 10 秒无法验证的播放器更新间隔使当前 Focus 失效；重启不会奖励未完成会话。Cancel 不发奖、不开始 Rest；End Focus Early 保留完整分钟的奖励。保存成功后才进入 Rest，失败会冻结结算并提供 Retry Save，避免重复发奖。Rest 不发奖，可提前结束；结束后提供再次 Focus 或返回 Dashboard。跨午夜 Focus 归入结算日。

存档升级到 version 3，新增每日奖励额度/Claim 状态和独立 Focus 会话收据。旧余额、Ticket、任务难度编号和历史实际奖励保留；已有当天任务收据计入额度，不补发新旧奖励差额。

Unity 菜单 **Tools → Cat Quest → Verify Rewards and Focus** 验证奖励分档、上限、独立目标、撤销、午夜、Focus 结算/重试/取消/Rest、迁移及等级，并运行现有任务和 CharacterHome 检查。结果位于 `Temp/reward-system.verification`。

Unity 菜单 **Tools → Cat Quest → Verify Dashboard Rewards Play Flow** 在实际 Play Mode 中使用隔离存档，验证 Claim、Focus 快捷入口、页面切换、HUD/等级刷新、Rest 与取消，并检查 1280×720 / 1920×1080 的界面。截图位于 `Library/DashboardRewardPlayVerification/<GUID>/`，结果位于 `Temp/dashboard-reward-play.verification`。共享界面保存在 Canvas prefab，可在编辑器直接调整；`Rebuild Dashboard Reward UI` 可按现有资源重新建立本次界面。

## 可重复验证

Unity 菜单 **Tools → Cat Quest → Verify Task System**：
验证 CRUD、草稿隔离、筛选、标签关联、奖励去重、撤销／重新完成、历史恢复限制、按日期逾期及排序、旧时间兼容、午夜、未来开始日期、不补离线日期、旧存档迁移、失败回滚及备份恢复。

Unity 菜单 **Tools → Cat Quest → Verify MainMenu Play Flow**：
在 MainMenu 的 Edit Mode 执行，自动进入 Play Mode，验证实际 UI 操作与落盘，结束后退出 Play Mode。测试使用 `Library/TaskSystemPlayVerification/<GUID>/save.json`，不写入真实玩家存档。

测试报告位于 `Temp/task-system.verification` 和 `Temp/task-system.play-result`。

## Optional AI Chat

Chat can query tasks and propose new tasks or edits. Each proposal requires local
review and confirmation. Difficulty is manual: new tasks default to Medium and
edits preserve their previous value. Task completion/rewards never depend on AI.

`TaskService.ConfirmChatDraft` reuses the existing atomic upsert validation and
rejects stale, deleted, settled, duplicate and cross-day proposals. Task text is
only shared when a chat lookup requests it. See `AI_IMPLEMENTATION.md`.
