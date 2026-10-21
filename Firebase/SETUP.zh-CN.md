# Cat Quest：配置 Google 登录与 Firebase

当前 Windows 版本使用系统浏览器登录，随后通过 REST 连接 Firebase Authentication 和 Cloud Firestore。**不用安装 Firebase Unity SDK，也不用把 Firebase 的 JavaScript 示例粘进 Unity。** 登录不上传存档；只有手动开启排行榜参与后才上传八个汇总字段。

## 1. 创建 Firebase 项目并取得两个值

1. 打开 [Firebase Console](https://console.firebase.google.com/)，创建项目，例如 `Cat Quest`。Analytics 本功能不需要，可不启用。
2. 打开齿轮 **Project settings → General**，记下 **Project ID**。使用 ID，不是项目名称，也不是数字 Project number。
3. 在 **Your apps** 添加一个 **Web app（`</>`）**，例如 `Cat Quest Windows REST`；不用启用 Hosting。
4. 注册后，在 Firebase 配置代码中找到 `apiKey` 和 `projectId`。只需要把这两个值填到下面的 Unity 配置资产。

参考：[创建项目与注册 Web app](https://firebase.google.com/docs/web/setup)。注册 Web app 是为了取得本项目 REST 配置，不代表游戏改成网页。

## 2. 启用 Google 登录

1. Firebase Console → **Authentication → Get started**（已经开启时直接进入）。新版导航可能在 **Security** 下，旧版可能在 **Build** 下。
2. **Sign-in method → Google → Enable**。
3. 填写项目显示名、Support email，保存。
4. 保留 Firebase 自动配置的 **Web client ID / Web client secret**；不要把它们替换成下一步的 Desktop client。
5. Authentication 的 **Settings → Authorized domains** 保留 `<你的 Project ID>.firebaseapp.com`。应用的 Firebase 身份交换使用这个域名；浏览器回到本机则使用单独的 loopback 地址。

参考：[Firebase 启用 Google provider](https://firebase.google.com/docs/auth/web/google-signin)。

## 3. 创建 Windows 专用 OAuth Desktop client

1. 打开 [Google Cloud Console](https://console.cloud.google.com/)，顶部选择**与 Firebase 相同的项目**。
2. 打开 **Google Auth Platform**。若尚未配置，按引导填写 **Branding** 中的 App name、User support email、Developer contact email。
3. **Audience**：个人 Google 账号通常选 **External**。开发阶段保持 **Testing**，把你用来测试的 Google 邮箱加入 **Test users**。
4. **Data Access** 只需要基本身份范围 `openid`、`email`（控制台可能将 email 显示为 `.../auth/userinfo.email`）。本实现不请求 Drive、Calendar、联系人等权限。
5. **Clients → Create client → Application type: Desktop app**，名称例如 `Cat Quest Windows`。
6. 保存 **Client ID**，一般以 `.apps.googleusercontent.com` 结尾。若创建结果提供 Desktop client secret，也保留它用于对应的可选字段。

应用会自动创建 `http://127.0.0.1:<随机端口>/catquest-oauth/<随机值>/` 回调。**不要创建 Web application client 来代替 Desktop app，也不用手动注册这个随机回调。** [Google Desktop OAuth 文档](https://developers.google.com/identity/protocols/oauth2/native-app) 支持 Windows loopback 和 PKCE。

如果 Firebase 拒绝 Desktop ID 对应的 Google 凭证，核对两个 client 是否属于同一项目；在 Google provider 的 allowed/external client IDs 配置中加入 Desktop Client ID。控制台提供此项时可在 Google Cloud **Identity Platform → Providers → Google → external client IDs** 设置；保留原 Web client。见 [Google provider 外部 client ID 配置](https://docs.cloud.google.com/identity-platform/docs/web/google)。无需为了获得普通 Google 登录而自行开启其他付费功能。

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

已有 `firebase.json`，不用重新运行 `firebase init`。排行榜还需要四个复合索引；建议使用以上 CLI 命令同时部署规则和 firestore.indexes.json，并等待索引状态变为 Enabled。规则允许已登录用户读取排行榜汇总，只有本人可以修改自己的记录，禁止匿名读取及额外私有字段；不要改成公开读写来排错。[规则发布说明](https://firebase.google.com/docs/rules/manage-deploy)

## 5. 在 Unity 填写配置

退出 Play Mode，在 Project 窗口选中 **`Assets/Resources/AccountConnectionConfig.asset`**，填写 Inspector：

| Inspector 字段 | 填入内容 |
|---|---|
| Firebase Project Id | 第 1 步的 `projectId` |
| Firebase Api Key | 第 1 步 Web app 配置中的 `apiKey` |
| Google Desktop Client Id | 第 3 步 **Desktop app** 的 Client ID |
| Google Desktop Client Secret | 可选；Google 要求该 Desktop client 的 secret 时填写该 client 自己的值 |

不要填写 Firebase App ID、项目数字编号、Web client secret 或服务账号私钥。Desktop client 的 secret 不能作为服务器机密使用；本项目绝不需要 service-account JSON。保存资产后重新构建 Windows，已有 exe 不会自动读取刚修改的 Unity 资产。

## 6. 按顺序确认配置成功

如果要完全隔离日常进度，可在另一个 Windows 测试用户下运行构建，它有独立的本地存档和 Credential Manager。只复制 exe 到另一个文件夹不会隔离存档；`--focus-smoke` 则会跳过真实凭证恢复，不适合验证登录重启。不要删除原存档来做测试。

1. 用专门的测试存档打开新构建，进入 **Settings → Account → Sign in with Google**，保持 Account 弹窗打开，完成系统浏览器登录；关闭弹窗会取消尚未完成的登录。
2. 返回应用，确认显示已登录。Firebase **Authentication → Users** 应能看到该用户。此时不应上传任务或整份存档。
3. 输入自己确认的排行榜名称（去除两端空白后 1–20 字符），开启参与开关。**首次开启会把这份本地存档永久绑定到当前 UID**；不要拿日常存档试多个不同账号。
4. 在 Firestore **Data → leaderboard → 当前 Firebase UID** 查看记录，应只有：`playerId`、`playerName`、`bossStage`、`totalTasksCompleted`、`totalFocusMinutes`、`power`、`catLevel`、`updatedAt`。
5. 完成一个任务或有效 Focus，检查汇总更新；退出并重启，检查登录恢复；Sign out 后检查本地任务仍在；用原账号登录后关闭参与，确认文档删除。断网时会显示等待移除，联网并认证后才完成。

排行榜页面已实现四种排名及个人名次；登录后可读取已发布的汇总数据。云存档备份和换设备恢复不在当前范围内。规则只开放排行榜汇总读取，其他玩家不能修改你的记录。

## 常见问题

| 现象 | 检查 |
|---|---|
| Google 按钮不可用 | 三个必填字段是否齐全、是否保存并重新构建 |
| Google 提示 `access_denied` | 当前邮箱是否是 Test user；若是学校账号，检查管理员限制 |
| `redirect_uri_mismatch` / client 无效 | Unity 是否误填 Web client ID；确保类型为 Desktop app，且 ID 未被删除 |
| 浏览器成功但 Firebase 登录失败 | Google provider 是否启用、API key 与 Project ID 是否来自同一项目、Desktop ID 是否允许、是否需要 Desktop secret |
| API key 被拒绝 | 检查 Google Cloud API key 限制是否允许 Authentication REST 所需 API；不要对桌面 REST 请求设置网站 HTTP referrer 限制 |
| 登录成功，上传 Permission denied | 检查 `(default)` 数据库、已发布的规则，以及当前账号是否为这份存档绑定的 UID |
| 登录超时 | 两分钟内完成浏览器操作，保持 Account 打开；检查本机 loopback 是否被安全软件阻拦 |
| 显示其他设备冲突 | 在 Account 比较统计后明确选择是否用本机数据替换；不要修改规则绕过版本检查 |

真实 Google/Firebase 登录必须在上述配置完成后实测；本地 mock 和 emulator 通过不能代替云端联调。英文实现细节、边界情况与验证入口见 [SETUP.md](SETUP.md)。


## 本机配置与 Git

`Assets/Resources/AccountConnectionConfig.asset` 及其 `.meta` 仅保存在本机，已由 Git 忽略。不要提交已填好的 OAuth 配置。首次检出时，可将 `Firebase/AccountConnectionConfig.asset.example` 复制到上述 asset 路径，让 Unity 生成 `.meta`，再在 Inspector 填写配置；也可通过 Create > Cat Quest > Account Connection 创建。现有本机配置不需要重新创建。
