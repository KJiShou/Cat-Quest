# AI 聊天与任务提议

[English](AI_IMPLEMENTATION.md) | [简体中文](AI_IMPLEMENTATION.zh-CN.md)

## 产品行为

Chat 已替代自动难度建议，保留浏览器登录、断开、模型选择和额度管理。旧 automatic 偏好忽略，不后台分析任务。新建议默认 Medium，编辑保留原手动难度，仅用户可在审阅表单修改。

Chat 支持对话、任务查询、创建提议和有效任务编辑，不完成、删除任务或发奖。所有提议通过已有编辑器审阅并由 TaskService 提交。Today 查询与任务页一致，含有效每日和逾期日程。日期、名称、ID 查询限制在请求条件，每次最多 40 项并提示截断，每轮最多三次查询。任务文本是数据，不是指令或可执行写入。

JSON 响应包含 message、可选 query 和 drafts，查询支持 today/date/search/id。编辑目标必须本轮读过，保留未改字段和子任务身份。包括 difficulty、rewards 在内的不支持字段被拒绝，现有标签和手动难度由本地审阅修改。

确认复用原子任务保存，防止目标已改变、删除、结算或跨日。新草稿创建任务前已持久化稳定 ID，重复保存不新增第二个任务。写入失败保留本地数据，完成和奖励规则不变。

## 对话与隐私

仅发送用户消息和请求的任务事实，使用已连接 ChatGPT 计划。凭证受 Windows DPAPI 保护，单个对话独立保存在同样保护的 `ai/chat.dpapi`，不改玩家 schema。清聊天不删任务。

请求最多包含最近 20 条消息和 16,000 文本字符，每条 2,000 字符。旧对话仍本地可见，但未必发送，当前事实通过新查询获取。同时仅一个请求，停止、断开、换模型和清空会丢弃迟到响应，切页保留会话，失败可重试。

主 Unity 进程拥有查询和确认，伙伴不初始化 AI。测试用注入存储或隔离目录，不用真实存档。普通 JSON 上限四百万字符，传输另有 SSE/输出限制和 20 秒期限。

## UI 与实现

参考 SimpleCasual `Preview/042_Chat.png`、`Prefabs_DemoScene_Panels/Light/Caht.prefab`（原厂拼写）和 `Prefabs_Component_Frames/ChatFrame01,02_Light-Group.prefab`。项目 Chat、ChatMessage、ChatTaskDraft 复用任务编辑器、SidebarNavigationButton 和 JuicyButton，保留 Fredoka，并在 TMP Settings 全局注册项目自带的动态 Noto Sans CJK SC 中文备用字体。中文显示不依赖 AI 组件生命周期或电脑已安装的字体，不更换主字体或供应商资产。

从 Chat 打开 AI 设置，关闭返回 Chat 并恢复设置按钮焦点，从总设置打开保留正常返回。消息泡尺寸由可编辑 ChatMessage 文本内边距决定。导航枚举追加在 Leaderboard 后以保留旧索引，Canvas 只加入 Chat 页面按钮并删除旧 advice 子树，Settings 移除自动建议控制并更新隐私文案。

## 验证入口

- `Temp/verify-ai-data.request`：聊天领域、协议传输、绑定及 Rewards → Tasks/CharacterHome、账号设置回归。结果 `Temp/verify-ai-data.result`，证据 `Library/AiVerification/`。
- `Temp/verify-ai-play.request`：隔离模拟聊天、审阅确认、取消反馈、四分辨率和侧栏状态，结果 `Temp/verify-ai-play.result`。
- `Temp/build-settings-windows.request`：现有 Windows 开发构建。
- `--ai-smoke --smoke-dir "<absolute isolated directory>"`：需要 `ai-smoke.marker`，重定向存档与 AI 文件，等待用户浏览器授权，验证合成聊天后断开，不代替用户授权。
- `pwsh -NoProfile -File Tools/Verify-ChatGptOAuth.ps1 -SelfTest`：隔离离线 OAuth/协议。`-Run` 是独立真实授权实验。

旧报告只代表历史。当前是否通过以本次报告和观察为准。

## 已被替代的难度建议验证

以下为 **2026-10-24 历史记录**，不验证当前 Chat：

- 数据、协议、传输及任务奖励设置账号回归：`Library/AiVerification/Data-20261024-042727-29952f5aa36d4beda33e0d47637f5140/result.txt`。
- 1920×1080/1280×720 的真实 UI 与参考比较：`Library/AiVerification/Play-20261024-041302/`。
- 对象身份审计 `Temp/ai-serialized-review.json`，原对象未删除，构建工具重复运行验证后已移除。
- 零错误开发构建：`Builds/SettingsVerification/Build-20261024-042805/CatQuest.exe`。
- 当时的真实 Windows 流程：`Temp/AiWindowsSmoke/20261024-122146/result.json`。用户授权独立连接，重启恢复 DPAPI、读取模型、强制串行刷新、合成推理及撤销断开。`ai-player.png` 为终态，测试 token 已清除，仅留加密注册。`live-save-after.json` 记录真实存档 SHA-256 未变。

账号模型目录超过原 256 KiB HTTP/JSON 上限后，两个限制一起修正并回归重建。更早失败保留为诊断，不能算通过。真实拒绝、额度耗尽、服务中断和两分钟超时未全部实际触发，负例采用合成数据。Google 回归使用既有验证器，不是新的真实 Google/Firebase 登录。这仍为 Windows 本地原型。

## 官方协议参考

- [登录与公共客户端注册](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)
- [模型与推理](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
- [预览限制](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations)
- [账号与会话](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions)

## Chat 历史验证，2026-10-24

领域与账号设置回归见 `Library/AiVerification/Data-20261024-063908/result.txt`，Play 与已审阅截图见 `Library/AiVerification/Play-20261024-063950/`。`Builds/SettingsVerification/Build-20261024-064040/` 当时零错误。隔离 Windows 批处理模拟聊天及重启通过，但不证明真实推理或原生交互。离线模式为带标记的 `--ai-smoke --ai-chat-offline --smoke-dir <isolated-directory>`，普通模式仍需用户授权。完整证据、限制与源哈希在 `Library/AiVerification/Delivery-20261024-0644/`。

## Sensei 聊天表现

头部与助手模板使用现有 Sensei idle 纹理和可编辑圆形脸部遮罩，角色是耐心导师，权限不变。三个 Chat 预制体保留对象绑定，页面内边距露出应用背景。参考 042_Chat、ChatFrame01,02_Light-Group、Caht/CharacterFrame，控制与草稿用任务图标，保留 Fredoka/JuicyButton。

表现证据为 `Library/AiVerification/Sensei-20261024/report.md`，较早 Windows 构建不含该视觉改进，该阶段只在 Editor 检查。

## 模型下拉与 Windows 后续

模型模板有序列化 Toggle 项与 TMP 文本，Play 在两种标准尺寸明确开关列表。2026-10-24 曾实测连接恢复、token 刷新、推理解析及成功断开。`Library/AiVerification/Dropdown-20261024/report.md` 保留证据、限制及早期无效聚合结果与修正说明。

## 语言、标签与聊天表现

玩家头像在右、Sensei 在左，名字较大且独立，正文为纯文本，头部仅 Sensei。禁用控制灰色，等待文字按字形顶点使用未缩放时间波动，停止禁用时复位。移除闲置提醒和审阅说明行，保留错误与提交结果。

草稿复用难度徽章、日程子任务标签图标及彩色标签。审阅使用已有编辑器，在居中有界面板、全 Canvas 遮罩和固定页脚中仅滚动表单。

Reply language 支持 Auto（最新用户消息）、English、Simplified Chinese，偏好独立于玩家数据，旧连接默认 Auto 不重置登录。明确语言优先于历史语言，换语言模型会取消等待请求，引用原任务标题不改原文。

有界 `tags` 查询只提供已有 ID/名称，草稿只能选择存在的 tagIds，未知拒绝，编辑省略 tagIds 保留旧标签。AI 不建标签、不选难度、不发奖。历史证据在 `Library/AiVerification/ChatPolish-20261024/report.md`。

### 每轮语言漂移保护

Auto 每轮仅按最新用户消息判断中英文，明确设置优先，任务标签查询和重试复用本轮语言，不从返回数据重新判断。其他文字系统由模型处理，这个简单规则不是通用语言检测器，例如不能区分英法。

最终回复出现明显的英文/中文模式西里尔文漂移，或英文模式汉字漂移时，使用相同事实重生成一次。用户原文和查询文本豁免以保留任务名字。第二次仍不符或纠正时额外查询则失败并显示 Retry，拒绝的消息草稿不保存，取消丢弃迟到纠正。已有历史，包括旧错误语言回复，继续保留。
