# Windows 文件夹版更新

[English](UPDATES.md) | [简体中文](UPDATES.zh-CN.md)

正式 Windows 版本启动后会在后台检查一次更新。用户也可以打开 **Settings → Updates** 检查、下载，再选择 **Restart & update**。下载的是完整程序 ZIP，不是增量补丁；用户无需手动解压覆盖。关闭弹窗不取消下载。Editor、Development Build 和测试模式不会自动联网检查。

更新源固定为现有 `KJiShou/Cat-Quest` 仓库的公开 Releases。不包含 GitHub token，也不会修改仓库可见性或自动发布。

## 发布新版

1. 在 Unity Player Settings 中设置更高的数字版本，例如 `1.2.1`。保持 Company Name、Product Name 不变，避免改变存档和 PlayerPrefs 的位置。
2. 使用现有 Windows 构建入口，例如创建 `Temp/build-focus-windows.request`。构建后自动生成 `CatQuestUpdater.exe` 和版本标识文件。更新器使用 Windows .NET Framework，用户不需要安装 .NET SDK。
3. 将更新说明保存为 UTF-8 文件，并在项目根目录执行：

   ```powershell
   ./Tools/Publish-CatQuestPackage.ps1 -BuildDirectory Builds/Windows -OutputDirectory Builds/Releases/1.2.1 -NotesFile Builds/release-notes.txt
   ```

4. 在 GitHub 创建标签为 `v1.2.1` 的公开正式 Release，上传生成的 `CatQuest-1.2.1-win-x64.zip` 和 `catquest-update.json`。两份文件上传完毕后再发布，并设为 latest。打包脚本本身不发布任何内容。
5. 首次分发使用生成的 ZIP。没有更新器的旧版用户需要手动换成一次这个版本，之后才能游戏内更新。每次发布前应从上一个正式包实测升级。

私有仓库的 Releases 不能供普通用户匿名下载。更新地址是 `https://github.com/KJiShou/Cat-Quest/releases/latest/download/catquest-update.json`，不要将访问私有仓库的凭据打进游戏。正式打包默认拒绝 Development Build。

## 安装与数据安全

- Focus、待结算、Rest、战斗进行中或存档只读时，暂不允许重启安装。更新不会取消 Focus 或修改奖励规则。
- 安装前保存成功、关闭所属 Companion 并确认它已退出，再由独立更新器等待主程序退出。Companion 与主程序使用同一个 EXE，因此一起更新。
- 更新包通过 HTTPS 从指定仓库下载，并检查 ZIP 的 SHA-256、大小以及每个程序文件。SHA-256 用于完整性校验；发布者身份信任来自 HTTPS 和 GitHub 仓库控制权，没有另外的数字签名。
- 只替换发布清单中的程序文件；删除旧清单中已废弃的程序文件。遇到用户文件冲突就停止，不覆盖用户额外放入的文件。
- 不修改 `catquest-data.json`、登录信息或玩家数据结构，也不清空整个程序目录。安装目录应位于普通用户可写的位置，更新器不会请求管理员提权。

## 失败恢复

普通安装错误会尝试恢复旧程序文件。备份和事务记录保存在安装目录旁边的 `<安装目录>.update-history/<事务编号>`，日志位于本次更新缓存中的 `result.txt`。

若更新器或电脑在替换途中退出，先关闭所有 Cat Quest 和 Companion，再使用缓存中的完整更新器恢复：

```powershell
& 'C:\更新缓存\CatQuestUpdater.exe' --recover 'C:\游戏目录\CatQuest'
```

恢复可以重复执行，只撤销未完成的安装，不回退已经成功安装的版本，也不恢复旧存档。磁盘故障或文件被占用时恢复仍可能失败，保留日志和备份以便排查。当前下载缓存与旧程序备份需要手动清理，建议确认新版可用后再清理。

“安装成功”表示文件安装完成并已请求启动新版，不等于已经自动检查新版全部功能。

## 验证入口

- `Tools/Updater/Verify-Updater.ps1`：版本、路径、ZIP、校验、重复调用、失败回滚、重试和中断恢复。
- `Tools/Updater/Verify-Handoff.ps1`：独立测试进程的身份握手、错误身份拒绝、自身更新和重启。
- 向 `Temp/verify-settings-play.request` 写入 `app-updates`：隔离 Play Mode 界面检查及 1920×1080、1280×720 截图。
- 创建 `Temp/build-app-updates.request`：顺序构建 1.2、1.2.1 两个 Windows 测试版本，完成后恢复项目版本配置；路径见同名 `.result`。
- `Tools/Verify-AppUpdatesWindows.ps1 -BuildRoot <构建结果目录>`：实际 Unity 和 Companion 的完整包更新，使用隔离存档并恢复 PlayerPrefs。

测试结果位于忽略目录 `Library/`、`Temp/`、`Builds/`。首次上传正式 Release 后，还需要验证公开 GitHub 的实际下载链路。

建议安装到较短且可写的路径，例如 `%USERPROFILE%\Games\CatQuest`。.NET Framework 更新器受 Windows 传统路径长度限制，备份目录也会增加路径深度。目录过深可能导致更新失败；关闭程序后把整个应用文件夹移到较短路径，再重试。公开发布验证中，长路径失败后的旧版文件清单与哈希保持完整。

需要指定正式构建目录时，向 `Temp/build-app-release.request` 写入 JSON，包含 `version`、绝对路径 `outputDirectory`（空目录或新目录）和 `restoreVersion`。现有 Editor 使用 `BuildOptions.None` 构建，结果写入 `Temp/build-app-release.result`。仅最终发布版本设置 `restoreVersion: false`，测试基线应恢复原版本。

`Tools/Verify-PublicUpdate.ps1 -Executable <已打包程序> -OutputDirectory <新证据目录>` 使用包含更新器的旧正式构建，实际访问公开 GitHub、下载并校验 ZIP、安装重启、检查隔离存档和 Companion。`-Mode Startup` 用于发布前启动检查，`-Mode Current` 检查已是最新版。所有模式使用隔离测试存档并恢复 PlayerPrefs；smoke 模式不会自动检查更新，因此网络验证明确调用正常的 Check/Download/Install 方法。
