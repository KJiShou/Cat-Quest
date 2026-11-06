# Cat Quest：Mac 本地测试版

这是面向 Apple Silicon（M 系列）、macOS 12 或更高版本的桌面版，使用 Unity 6000.3.10f1 / Mono 构建。功能范围以离线任务与成长为主，不提供 Pet Widget。

## 在 Mac 上运行

1. 把生成的整个 `CatQuest-Mac-*.zip` 复制到 Mac，再用访达解压。不要在 Windows 解压后只复制应用的可执行文件。
2. 将 `Cat Quest.app` 放到你选择的文件夹，双击启动；无需安装 Unity 或 Xcode。
3. 这是尚未经过 Apple Developer ID 签名和公证的测试包。如果 macOS 阻止打开，在确认是自己构建的文件后，使用「系统设置 → 隐私与安全性 → 仍要打开」。参见 [Apple 官方说明](https://support.apple.com/zh-cn/102445)。

ZIP 保留了 Unix 执行权限。如果其他传输或解压工具丢失权限，在终端输入 `chmod +x `，将 `Cat Quest.app/Contents/MacOS/Cat Quest` 拖到命令后面，再执行。

## 功能范围

- 保留手动任务、Focus / Rest、奖励、角色成长、装备、Boss 和本地存档。End Early 和取消规则不变。
- Pet Widget、Windows 桌面通知、Windows 自动更新不可用。
- 与 Web 版一样，ChatGPT、Google 登录和排行榜联网入口不可用。
- Mac 使用本机 JSON 存档，与浏览器存档独立。当前 Mac 版不提供系统文件选择器，因此设置中的备份导入和导出不可用；这是与 Web 下载备份的一个差异。
- 退出应用后，可通过访达「前往文件夹」打开 `~/Library/Application Support/DefaultCompany/Cat Quest/`，复制 `catquest-data.json` 做手动备份。不要在应用运行期间替换存档。

## 还需要在 Mac 实机检查

Windows 上的成功构建不能证明 macOS 的运行表现。首次测试建议依次检查：

1. 启动、输入名字、进入主界面；1280×720 与 1920×1080 下的布局、字体和点击。
2. 创建并完成任务，退出重开后任务和奖励保留，没有重复奖励。
3. Focus 开始、End Early、取消、Rest；切换应用、最小化和恢复后的计时。
4. 音乐/音效、装备和 Boss；Pet Widget 不可开启。

出现问题时保留 `~/Library/Logs/DefaultCompany/Cat Quest/Player.log`，记录 macOS 版本、芯片和复现步骤。请勿把真实任务内容放入公开日志。

## 从 Windows 重新构建

1. Unity Hub 为 **6000.3.10f1** 安装 **Mac Build Support (Mono)**，然后重启当前项目的 Editor。
2. 保存场景，退出 Play Mode。使用 `Tools → Cat Quest → Build Mac Version (Apple Silicon, No Widget)`。
3. 工具使用已有启用场景，生成 `Builds/CatQuest-Mac-时间戳/Cat Quest.app`；完成后恢复原构建平台、后端与架构。结果在 `Library/MacBuildVerification/build-result.txt`。
4. 使用 Python 3.11+ 运行 `python Tools/Package-MacBuild.py Builds/CatQuest-Mac-时间戳`，生成同名 ZIP。工具核对 ARM64 主程序、Mono 程序集、ZIP CRC、执行权限，并输出 SHA-256；不覆盖已有 ZIP。

这份 ZIP 可以作为 itch.io 的 **macOS 下载文件**，不是「在浏览器中运行」文件。公开发布前先完成 Mac 实测；面向广泛分发的 Developer ID 签名和公证仍需另外处理。

参考：[Unity macOS 构建说明](https://docs.unity3d.com/6000.3/Documentation/Manual/macos-building.html)、[Unity macOS 分发](https://docs.unity3d.com/6000.3/Documentation/Manual/macos-delivery.html)。
