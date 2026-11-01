# Cat Quest：配置 Google 登录与 Firebase

[English](SETUP.md) | [简体中文](SETUP.zh-CN.md)

当前 Windows 版本使用系统浏览器登录，随后通过 REST 连接 Firebase Authentication 和 Cloud Firestore。**不用安装 Firebase Unity SDK，也不用把 Firebase 的 JavaScript 示例粘进 Unity。** 登录不上传存档。只有手动开启排行榜参与后才上传八个汇总字段。

## 1. 创建 Firebase 项目并取得两个值

1. 打开 [Firebase Console](https://console.firebase.google.com/)，创建项目，例如 `Cat Quest`。Analytics 本功能不需要，可不启用。
2. 打开齿轮 **Project settings → General**，记下 **Project ID**。使用 ID，不是项目名称，也不是数字 Project number。
3. 在 **Your apps** 添加一个 **Web app（`</>`）**，例如 `Cat Quest Windows REST`。不用启用 Hosting。
4. 注册后，在 Firebase 配置代码中找到 `apiKey` 和 `projectId`。只需要把这两个值填到下面的 Unity 配置资产。

参考：[创建项目与注册 Web app](https://firebase.google.com/docs/web/setup)。注册 Web app 是为了取得本项目 REST 配置，不代表游戏改成网页。

## 2. 启用 Google 登录

1. Firebase Console → **Authentication → Get started**（已经开启时直接进入）。新版导航可能在 **Security** 下，旧版可能在 **Build** 下。
2. **Sign-in method → Google → Enable**。
3. 填写项目显示名、Support email，保存。
4. 保留 Firebase 自动配置的 **Web client ID / Web client secret**。不要把它们替换成下一步的 Desktop client。
5. Authentication 的 **Settings → Authorized domains** 保留 `<你的 Project ID>.firebaseapp.com`。应用的 Firebase 身份交换使用这个域名。浏览器回到本机则使用单独的 loopback 地址。

参考：[Firebase 启用 Google provider](https://firebase.google.com/docs/auth/web/google-signin)。

## 3. 创建 Windows 专用 OAuth Desktop client

1. 打开 [Google Cloud Console](https://console.cloud.google.com/)，顶部选择**与 Firebase 相同的项目**。
2. 打开 **Google Auth Platform**。若尚未配置，按引导填写 **Branding** 中的 App name、User support email、Developer contact email。
3. **Audience**：个人 Google 账号通常选 **External**。开发阶段保持 **Testing**，把你用来测试的 Google 邮箱加入 **Test users**。
4. **Data Access** 只需要基本身份范围 `openid`、`email`（控制台可能将 email 显示为 `.../auth/userinfo.email`）。本实现不请求 Drive、Calendar、联系人等权限。
5. **Clients → Create client → Application type: Desktop app**，名称例如 `Cat Quest Windows`。
6. 保存 **Client ID**，一般以 `.apps.googleusercontent.com` 结尾。若创建结果提供 Desktop client secret，也保留它用于对应的可选字段。

应用会自动创建 `http://127.0.0.1:<随机端口>/catquest-oauth/<随机值>/` 回调。**不要创建 Web application client 来代替 Desktop app，也不用手动注册这个随机回调。** [Google Desktop OAuth 文档](https://developers.google.com/identity/protocols/oauth2/native-app) 支持 Windows loopback 和 PKCE。

如果 Firebase 拒绝 Desktop ID 对应的 Google 凭证，核对两个 client 是否属于同一项目。在 Google provider 的 allowed/external client IDs 配置中加入 Desktop Client ID。控制台提供此项时可在 Google Cloud **Identity Platform → Providers → Google → external client IDs** 设置。保留原 Web client。见 [Google provider 外部 client ID 配置](https://docs.cloud.google.com/identity-platform/docs/web/google)。无需为了获得普通 Google 登录而自行开启其他付费功能。

## 4. 创建 Firestore 并发布项目规则

1. Firebase Console → **Firestore Database**（新版在 **Databases & Storage → Firestore**）。
2. 创建 **Standard edition** 数据库，ID 为 **`(default)`**，选择合适的区域。
3. 初始规则选择 **Production mode**。
4. 进入该数据库的 **Rules**，将仓库 [`firestore.rules`](firestore.rules) 的完整内容复制进去，点击 **Publish**。无需手工创建 `leaderboard` 集合，首次允许上传后程序会创建自己的文档。

也可以用 Firebase CLI 发布同一份规则。在 PowerShell 执行以下命令，将 `你的-project-id` 换成真实 Project ID：

```powershell
Set-Location 'D:\JiShou\Unity\Cat Quest\Firebase'
npx firebase-tools login
npx firebase-tools deploy --only firestore:rules,firestore:indexes --project '你的-project-id'
```

已有 `firebase.json`，不用重新运行 `firebase init`。排行榜还需要四个复合索引。建议使用以上 CLI 命令同时部署规则和 firestore.indexes.json，并等待索引状态变为 Enabled。规则允许已登录用户读取排行榜汇总，只有本人可以修改自己的记录，禁止匿名读取及额外私有字段。不要改成公开读写来排错。[规则发布说明](https://firebase.google.com/docs/rules/manage-deploy)

## 5. 在 Unity 填写配置

退出 Play Mode，在 Project 窗口选中 **`Assets/Resources/AccountConnectionConfig.asset`**，填写 Inspector：

| Inspector 字段 | 填入内容 |
|---|---|
| Firebase Project Id | 第 1 步的 `projectId` |
| Firebase Api Key | 第 1 步 Web app 配置中的 `apiKey` |
| Google Desktop Client Id | 第 3 步 **Desktop app** 的 Client ID |
| Google Desktop Client Secret | 可选。Google 要求该 Desktop client 的 secret 时填写该 client 自己的值 |

不要填写 Firebase App ID、项目数字编号、Web client secret 或服务账号私钥。Desktop client 的 secret 不能作为服务器机密使用。本项目绝不需要 service-account JSON。保存资产后重新构建 Windows，已有 exe 不会自动读取刚修改的 Unity 资产。

## 6. 按顺序确认配置成功

如果要完全隔离日常进度，可在另一个 Windows 测试用户下运行构建，它有独立的本地存档和 Credential Manager。只复制 exe 到另一个文件夹不会隔离存档。`--focus-smoke` 则会跳过真实凭证恢复，不适合验证登录重启。不要删除原存档来做测试。

1. 用专门的测试存档打开新构建，进入 **Settings → Account → Sign in with Google**，保持 Account 弹窗打开，完成系统浏览器登录。关闭弹窗会取消尚未完成的登录。
2. 返回应用，确认显示已登录。Firebase **Authentication → Users** 应能看到该用户。此时不应上传任务或整份存档。
3. 输入自己确认的排行榜名称（去除两端空白后 1–20 字符），开启参与开关。**首次开启会把这份本地存档永久绑定到当前 UID**。不要拿日常存档试多个不同账号。
4. 在 Firestore **Data → leaderboard → 当前 Firebase UID** 查看记录，应只有：`playerId`、`playerName`、`bossStage`、`totalTasksCompleted`、`totalFocusMinutes`、`power`、`catLevel`、`updatedAt`。
5. 完成一个任务或有效 Focus，检查汇总更新。退出并重启，检查登录恢复。Sign out 后检查本地任务仍在。用原账号登录后关闭参与，确认文档删除。断网时会显示等待移除，联网并认证后才完成。

排行榜页面已实现四种排名及个人名次。登录后可读取已发布的汇总数据。云存档备份和换设备恢复不在当前范围内。规则只开放排行榜汇总读取，其他玩家不能修改你的记录。

## 常见问题

| 现象 | 检查 |
|---|---|
| Google 按钮不可用 | 三个必填字段是否齐全、是否保存并重新构建 |
| Google 提示 `access_denied` | 当前邮箱是否是 Test user。若是学校账号，检查管理员限制 |
| `redirect_uri_mismatch` / client 无效 | Unity 是否误填 Web client ID。确保类型为 Desktop app，且 ID 未被删除 |
| 浏览器成功但 Firebase 登录失败 | Google provider 是否启用、API key 与 Project ID 是否来自同一项目、Desktop ID 是否允许、是否需要 Desktop secret |
| API key 被拒绝 | 检查 Google Cloud API key 限制是否允许 Authentication REST 所需 API。不要对桌面 REST 请求设置网站 HTTP referrer 限制 |
| 登录成功，上传 Permission denied | 检查 `(default)` 数据库、已发布的规则，以及当前账号是否为这份存档绑定的 UID |
| 登录超时 | 两分钟内完成浏览器操作，保持 Account 打开。检查本机 loopback 是否被安全软件阻拦 |
| 显示其他设备冲突 | 在 Account 比较统计后明确选择是否用本机数据替换。不要修改规则绕过版本检查 |

真实 Google/Firebase 登录必须在上述配置完成后实测。本地 mock 和 emulator 通过不能代替云端联调。实现边界和隔离验证见下文。

## 本机配置与 Git

`Assets/Resources/AccountConnectionConfig.asset` 及其 `.meta` 仅保存在本机，已由 Git 忽略。不要提交已填好的 OAuth 配置。首次检出时，可将 `Firebase/AccountConnectionConfig.asset.example` 复制到上述 asset 路径，让 Unity 生成 `.meta`，再在 Inspector 填写配置。也可通过 Create > Cat Quest > Account Connection 创建。现有本机配置不需要重新创建。

## 实现边界与数据一致性

仓库不提供已部署的 Firebase 项目或云端凭证。Google 浏览器登录使用授权码和 S256 PKCE，Firebase Authentication 与 Firestore 使用 REST，不需 Unity SDK。API key 和 client ID 是项目标识，不是管理凭证。刷新 token 仅在 Windows Credential Manager，按项目与本地存档位置区分，访问 token 只留内存，不记录 OAuth 回调或 token。

首次参与绑定的 UID 在退出后保留，当前阶段没有存档转移或每账号独立本地档案。云数据仅是可替换排名快照，规则验证所有权和结构，不能证明离线进度真实，不是防作弊后端。

普通 Boss 统计报告已解锁最高 Boss，Boss 3 获胜后为 `3 + current masterStage`。Power 使用当前最终装备属性。任务总数是不同且未撤销完成执行数，包含达到奖励上限后的完成。Focus 汇总不同有效 Completed/EndedEarly 的有效秒数再向下取分钟，缺失时用旧有效分钟，奖励额度不减少该统计。

每账号一个快照，以 Firestore updateTime 前置条件写入。另设备发布暂停本机并要求比较替换，不相加合并。换装备和撤销任务可合法降低统计。退出分享立即停止新上传并排队删除，离线删除在原账号可用后完成。曾发布设备看到记录远端被删，应进入冲突而不是偷偷重建。

首次创建响应丢失且读不到文档时，客户端无法证明请求不会迟到，因此有界读取并保持发布或删除待定，不重复创建或声称已删。读到完全相同待发快照才协调，极端情况需检查测试项目和服务请求。八字段方案不增加服务端同意或墓碑字段。上传中关闭分享先等当前结果再删除。

已登录者可读和列出发布汇总，匿名读和他人写被拒绝，四索引支持独立排名。参考[排行榜](../LEADERBOARD.zh-CN.md)。

## 隔离验证

只有明确授权的真实测试才使用开发版 `--account-smoke --account-test-dir "<absolute isolated directory>"`。先创建 account-test.marker，程序改用 account-test-save.json 和独立 Credential Manager 目标，打开原 Account，输出不含 token 的 account-result.json。非开发版禁用，不自动登录、参与、替换冲突或删除云记录。

浏览器由用户授权，account-command.json 调用既有控制器，verify 在进程内读取已认证文档，只输出状态、字段白名单和相等性，不输出响应体凭证。使用新目录、测试账号，仅清理该测试创建的记录后退出，通过启动器备份恢复 PlayerPrefs。

菜单 Tools > Cat Quest > Verify Settings Account 或 `Temp/verify-settings-account.request` 运行隔离汇总、OAuth、序列化、所有权、保存失败、发布协调、冲突删除。结果 `Temp/verify-settings-account.result` 与 `Library/SettingsAccountVerification`，不读真实凭证、不联网。

verify-rules.mjs 使用官方 @firebase/rules-unit-testing 检查本地规则，覆盖本人 CRUD、匿名和他人拒绝、必需和额外私密字段、数值名称边界、时间戳、四类查询、稳定同分和 50 名外精确排名。创建与更新都查非法载荷，拒绝非 loopback 和不以 demo- 开始的项目，只清空隔离模拟库，120 秒有界，断言失败返回失败状态。

在仓库根目录的 PowerShell 使用 Node.js 20+、Java 21+：

```powershell
$rulesRun = Join-Path (Get-Location) 'Temp/SettingsRulesVerification'
New-Item -ItemType Directory -Path $rulesRun -Force | Out-Null
Copy-Item -LiteralPath 'Firebase/firestore.rules' -Destination (Join-Path $rulesRun 'firestore.rules')
@'
{
  "firestore": { "rules": "firestore.rules" },
  "emulators": {
    "firestore": { "host": "127.0.0.1", "port": 8185 },
    "hub": { "host": "127.0.0.1", "port": 4405 },
    "logging": { "host": "127.0.0.1", "port": 4505 },
    "ui": { "enabled": false },
    "singleProjectMode": true
  }
}
'@ | Set-Content -LiteralPath (Join-Path $rulesRun 'firebase.json') -Encoding utf8
Push-Location $rulesRun
try {
  npm install --no-audit --no-fund --save-exact @firebase/rules-unit-testing@6.0.0 firebase@13.0.0
  if ($LASTEXITCODE -ne 0) { throw 'Test dependency installation failed.' }
  npx --yes firebase-tools@15.34.0 emulators:exec --only firestore --project demo-catquest-settings --config firebase.json 'node ../../Firebase/verify-rules.mjs'
  if ($LASTEXITCODE -ne 0) { throw 'Firestore rules verification failed。 inspect results/results.json.' }
} finally {
  Pop-Location
}
```

npm 文件、模拟器日志、results/results.json 与 JSON/HTML 覆盖率留在忽略的 Temp，记录原规则 SHA-256 和本次时间，不改项目依赖清单。运行器除启动副本外，每次显式加载 Firebase/firestore.rules。不需 Firebase 登录、真实项目或凭证。端口占用可改空闲本地端口，但主机必须 127.0.0.1。通过仅证明本地规则，不证明已部署规则、真实配置或 Google OAuth。见[官方模拟器测试](https://firebase.google.com/docs/firestore/security/test-rules-emulator)。

配置后另测浏览器同意取消超时、重启退出、错误账号、正常上传、离线重试、上传中退出、跨重启待删、另设备冲突替换及退出后的迟到回调。使用测试账号存档，不使用日常玩家数据。

实现参考：[Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app)、[Firebase Auth REST](https://firebase.google.com/docs/reference/rest/auth)、[外部客户端](https://cloud.google.com/identity-platform/docs/web/google)、[Firestore REST](https://firebase.google.com/docs/firestore/use-rest-api)、[写入前置条件](https://firebase.google.com/docs/firestore/reference/rest/v1/Precondition)、[服务器时间戳](https://firebase.google.com/docs/firestore/reference/rest/v1/Write)、[Unity 桌面限制](https://firebase.google.com/docs/unity/setup#desktop-workflow)。
