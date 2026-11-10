# Master Loke 教学与功能解锁

[English](TUTORIAL_SYSTEM.md) | [简体中文](TUTORIAL_SYSTEM.zh-CN.md)

## 产品行为

Tasks 从 Lv.1 可用，Focus 在 Lv.2，Boss 和 Leaderboard 在 Lv.5，所有新旧存档一致，不依赖教学完成。Dashboard、Tasks、Settings、Chat 和原装备解锁规则不变，后续 Boss 仍需前关胜利和 Lv.10/Lv.20，Master 仍需 Boss 3 胜利。

侧栏保留项目并显示 SimpleCasual 锁与悬停键盘解释，Dashboard 对应卡在可用前显示锁内容。导航、Focus 创建、Boss 入场和排行榜读取共用门槛。已有活动会话及战斗仍可结算、取消或恢复，不可验证中断后保留原语义。

新玩家跟随 Master Loke 创建真实任务，保存才推进，教学不会标记工作完成或发奖，首次真实奖励解释实际收据。升级后开放 Focus、Boss 和排行榜介绍，解锁提示等候奖励升级表现，不未经选择导航。每日奖励和装备解释观察真实条件。

教学使用 Back、Continue 和 Skip。Back 返回当前指南的上一段说明，第一步隐藏；只改变讲解进度，保留任务草稿、已保存任务、奖励、装备和进行中的会话。等待真实点击目标或任务保存时隐藏 Continue。Skip 停止自动教学但不绕过锁。Settings > Tutorial 可看主题和开始恢复 Tasks，锁主题只解释不打开功能。旧存档不自动重放已解锁介绍。

## 所有权与静态 UI

- `TaskService.Tutorial.cs` 处理门槛与原子教学写入，旧 PlayerData.tutorial 可空，教学有独立版本，存档 schema 仍为 5。
- TutorialController 观察已提交状态并协调，不发奖或确认游戏操作。
- StoryDialogueView 提供共用未缩放打字效果，保留 Story 移动、镜头、输入和结尾。
- `Assets/Prefabs/Tutorial/MasterTutorial.prefab` 是可编辑教学、导师、对话、帮助和高亮，Canvas 有序列化实例，不运行整页生成。
- FeatureLockUI 持有锁、提示、卡片表现及锁定控件键盘限制。
- CharacterHomePreview 为导师提供可选固定构图与动画参数，旧调用者仍默认。

TutorialSpotlight 在目标周围绘制暗罩并阻止其他指针，键盘只在允许目标及可见的 Back/Continue/Skip 内，关闭禁用失焦恢复原选择导航。说明步骤包含完成勾选框时仅高亮，不允许实际操作。创建任务和 Focus 设置启动仅放行对应控制。

打字时透明全屏输入盾接管点击，任意点击只补全当前句，不穿透下面控件。下一句开始时保留任务输入键盘焦点，避免草稿取消。SimpleCasual Tutorial_Focus_Icon_Up 标出可操作目标。任务页签、加号、History、Boss Stage/Challenge 和 Focus Start 都需玩家真实点击，纯说明用 Continue，不强制任务完成。

Tasks 教学接管输入时，切换焦点或回看页签不会保存快速新增草稿，Enter 仍可明确保存，因此 Back 会保留未写完的文字。关闭教学后释放接管，恢复普通的失焦、切页签及离页保存行为。

排行榜教学在已有 Account 上以三次 Continue 解释显示名、参与、隐私。名字、开关、登录、保存只读，不改偏好或认证，只关闭自己打开的 Account，教学时暂停页面竞争键盘导航。leaderboardStep 经原子路径保存。

## 分功能步骤

**Tasks 共九步**：加号、真实输入保存、完成勾选框、Today、Daily、Todo、Tags、搜索、History。先创建真实任务，再了解页签；目标点击和保存成功不能被 Continue 跳过。保存成功后仍停留在收据步骤，勾选只解释不完成。保留 taskStep 的真实保存收据及 taskPageStep 的内容身份，讲解顺序改为 5、6、7、0、1、2、3、4、8。旧的未保存指南先转到创建步骤，不删除草稿或任务。

这是可用性设计调整，尚未开展新用户实验；首次保存耗时、跳过率及理解度还没有实测结论。

**Boss 共七步**：当前敌人、猫生命属性、选关、后续条件、熟练度、Master、Challenge/券确认。玩家先点选关，再进行中间说明，换步、Skip、禁用、离页或失焦只关闭教学自己打开的面板，恢复 bossStep。Challenge 必须真实点，原确认是权威，教学不确认入场、不领熟练度，等级胜利条件保持。

Boss 熟练度与 Cat Home 背包说明明确每个普通 Boss 的固定奖励：5 胜武器、10 胜护甲、15 胜头饰，对应离线导师语音同步。说明不提供任选奖励，也不改变领取条件。

**Focus 共七步**：番茄钟目的、模式、专注时长、休息、标签、结束取消及单局完整 25 分钟 Ticket 规则、Start。短局可累加每日 Focus 目标，但不会合并计算 Ticket。Continue 推进说明，改设置不自动推进，最后需实际点击 Start Focus。进度原子保存，Help 重开序列，讲解结束不启动会话。

**Equipment 共七步**：熟练度和空背包、三槽、筛选、装备卸装、升级成本等级限制、Auto Select、套装。Continue 前进，Back 回看上一段。导航说明等 Continue 才进入 Cat Home，全程不启用装备操作或消费。equipmentStep 保存，Help > Rewards 进入并重启该指南，尊重既有已读收据。当前装备台词均已配上批准的离线 Master Loke 语音，返回步骤时通过 SettingsAudio 重播对应音频。

## 视觉参考

高亮保留 SimpleCasual 三层圆角，项目副本采用九宫边框和 Sliced Image 防拉伸，遮罩和输入限制不变。对白从 IntroStory DialoguePanel 复制并保留字体图形，参考 Light/Lobby_Tutorial_Message、Light/Popup_Tutorial、Tutorial_Focus_RoundSquare 和 PictoIcon_Lock_1。按钮沿用设置风格与 JuicyButton，Master Loke 使用 Sensei Sprites，打字时 sensei-talk，结束后 sensei-idle。

## 兼容与备份

不重置余额、历史、装备或成长。首次对有效旧存档写教学前，一次保存 `catquest-data.json.before-tutorial.bak`，同时保留原滚动 `.bak`。失败保持已提交玩法与教学，JsonUtility 复制显式保留旧 null 字段。

测试用 Library 隔离存档，禁止重置玩家存档演示。

## 验证

- `Temp/verify-tutorial.request`：有界规则和 Play，时间戳 `Library/TutorialVerification/`，恢复存档环境、起始场景、暂停和 Game View。
- `Temp/tutorial-rules.result`：等级、失败写入、任务收据、重复、旧会话恢复、Boss 前置、迁移备份重载及 TaskSystemVerification。
- 既有聚合 `Temp/verify-battle-rules.request` 包含 FocusImplementationVerification 和 RewardSystemVerification，Focus 数据从 Lv.2 并明确断言原等级奖励。

截图不能单独证明动效或交互，审查本次图像、录像、日志和结果。旧 PASS 不是当前证据。

Settings 教程显示真实设置界面，依次高亮通知、音效、音乐、排行榜、默认任务页、Dashboard 显示、标签、Account 和 AI。Dashboard 显示步骤另说明独立的 Reduce motion 设置只减少装饰效果，不改变进度。Continue 逐步介绍，不修改设置；结束或 Skip 后返回 Settings，失焦恢复后继续原步骤。

Tasks 在创建和完成勾选说明之后、分页介绍之前，新增 18 段右键菜单语音讲解：编辑、三档难度、保存、标签、日期快捷键／日历／清除日期、完成、删除及取消／关闭。复用 TaskContextMenu 预制体的教学专用实例，不绑定写入或操作回调，遮罩阻止菜单操作。Back／Continue／Skip 及失焦会释放演示，沿用旧步骤 ID，新步骤使用 9–26，不重置已完成的教程。任务不存在时使用不保存的示例。
