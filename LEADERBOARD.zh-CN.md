# 排行榜

[English](LEADERBOARD.md) | [简体中文](LEADERBOARD.zh-CN.md)

侧栏打开原 Canvas 页面内可编辑的 `Assets/Prefabs/Leaderboard/LeaderboardContent.prefab`。页签、行框和奖牌来自 SimpleCasual Light `Ranking.prefab`，保留 Fredoka 与 JuicyButton，不重建供应商或其他页面。

保留标题，不显示副标题或已发布人数。分类、Account/Refresh 按钮高度有界，Your Rank 预留标题和用户行区域。加载、空白、错误和参与提示留在原内容区，Refresh 薄荷色，Account 浅金色。从排行榜打开 Account 后，Back、Escape、背景或关闭返回排行榜并恢复 Account 焦点，从 Settings 打开则返回 Settings。

Dashboard 右下预览前三名，复用紧凑排名行。在 **Settings → Dashboard leaderboard** 从 Boss Stage、Tasks Completed、Focus Minutes、Power 选一项，立即本地保存，旧存档默认 Focus Minutes。标题、分值、图标同步，View All 打开相同分类。保存失败恢复原选择。共用账号校验、排序和读取器，打开、分类或账号上下文变化时刷新，关闭取消自己的读取，旧响应不能覆盖新分类。未登录、加载、空白、失败显示明确消息。统计为累计而非每周，不影响奖励规则或 Focus 额度显示。

**Settings → Show on Dashboard** 分别控制 Tasks、Daily rewards、Focus、Leaderboard、Character，默认全部显示且兼容旧存档。立即保存，失败恢复原值。隐藏卡退出布局，剩余分享空间，空行收起，全部隐藏时提示去设置。仅影响 Dashboard 表现，不改变任务、奖励、装备、计时或侧栏功能。隐藏排行榜取消预览读取但保留所选分类。

## 排名行为

- 四个分类各自降序。
- Power 同分按 Cat Level 降序再按稳定 player ID 升序，其他同分按 ID 升序。
- 进度 1–3 显示 Boss 1–3，4 起显示 Master 1 起。
- 列表前 50 名，固定 Your Rank 显示参与者真实名次，包括 50 名外。在榜内也标记 `(You)`。
- 当前用户的名次、名字、等级和分数用黑色粗体，复用行显示别人时恢复原样。
- 50 名外读取本人已发布记录，再按完整排序游标计数，所有请求使用首查询的服务器 readTime，不以未上传本地统计代替或编造零名次。
- 打开、换分类、Refresh、账号或发布状态变化触发读取，无持续轮询。关闭只取消该页面读取，不取消发布。
- 登录后可看榜而不参与，未登录、无人、未发布、退出、错误账号和读取失败状态明确。Account 使用既有设置模态框。

任务、专注、奖励和成长仍以本地为准，只读取发布既有八个汇总字段。显示名由用户选择，不上传 Google 姓名、邮箱或私密内容，不是服务器权威防作弊竞赛。

## Firebase 部署

旧的仅本人可读规则无法提供整页排名，需使用 Firebase 管理员登录部署新规则和四个复合索引，这与游戏 Google 登录不同。从项目根目录运行：

```powershell
Set-Location Firebase
npx firebase-tools login
npx firebase-tools deploy --only firestore:rules,firestore:indexes --project 'YOUR_FIREBASE_PROJECT_ID'
```

使用 AccountConnectionConfig 的项目 ID，等待四个索引 Enabled。规则允许已登录读取公开汇总、拒绝匿名读取，写入仅本人且字段严格限制。Unity 不应包含服务账号密钥。

本地编译和模拟器不部署配置。真实读取仍需云端规则索引、登录及按需发布的记录。见 [Firebase 配置](Firebase/SETUP.zh-CN.md)。

## 验证

- `Temp/verify-leaderboard-data.request`：隔离排序、响应解析、前列/本人/计数契约、失败、退出参与、取消和预制体绑定，输出 `Library/LeaderboardVerification/Data-*` 及 REST 契约。
- `Temp/verify-leaderboard-play.request`：已打开 Editor、隔离存档，四种标准尺寸及双侧栏，参考比较、奖牌居中、控件边界、分类、138 名、空白错误退出、快速切换、重开及 Account，恢复 Game View、起始场景与环境。
- `Firebase/verify-rules.mjs`：模拟器所有权隐私、四类前 50 与精确名次。可将 `CATQUEST_QUERY_CONTRACTS` 指向绝对 `query-contracts.json`，额外验证编译 C# 请求形状。这些额外形状检查使用模拟器 owner token，登录匿名策略仍用官方规则测试上下文。
- 账号回归入口为 `Temp/verify-settings-account.request`。

模拟器与 Play 数据为合成，不发布真实玩家记录。键盘真人运动评审和真实 Firebase 读取须独立报告。

## Windows 表现检查

行背景 Pixels Per Unit Multiplier 为 2，奖牌数字按字形几何及奖牌面偏移对齐，普通名次居中。分类图标分别为 Boss、任务勾选、计时器和装备战力拳头，白色战力图在白底上着色以可读。

`Tools/Verify-LeaderboardWindows.ps1 -Executable '<development build>' -Run` 在独立可见 Windows 开发版检查 1920×1080/1280×720，使用本地预览数据，检查完整日志和 Development Console，截图、最小化恢复，并恢复 PlayerPrefs，不登录或发布。`Tools/Start-AccountVerification.ps1` 为复用交互启动器。

已有 6000.3.10f1 诊断中，先 Hidden 启动再 ShowWindow 会重现 PresentFrame/WaitForLastPresentation 警告，直接可见启动未出现。因此交互验证启动器使用 Normal，不关闭开发控制台或过滤错误掩盖失败。图形 API、Unity、通知和渲染配置不变。
