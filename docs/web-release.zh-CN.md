# 网页试玩版与 itch.io

网页试玩版与 Windows 版共用任务、奖励、装备、Boss 和 Focus 规则，面向桌面浏览器。
Windows 版保留完整桌面功能。可[在 itch.io 试玩](https://kjishou.itch.io/cat-quest)。另有独立的
[Mac 本地测试构建工具](mac-release.zh-CN.md)，尚待实机验证，不包含 Mac 桌面宠物窗口。

## 功能区别

- 保留手动任务、成长、装备、Boss，以及页面内的 Focus 和小猫动画。
- 隐藏桌面 Pet Widget 开关；ChatGPT、Google 桌面登录和依赖登录的排行榜不可用，原有面板显示说明。
- 不启动 Windows 自动更新；桌面通知和备份导入不可用。
- 设置中的 Export 会下载 JSON 备份，可在 Windows 版导入。
- Mac 可以尝试同一网页版；原生 Mac 测试版使用独立的 Mono / Apple Silicon 构建。

## 存档与专注

网页 JSON 存档和上一份有效备份会一起写入浏览器本地存储。浏览器拒绝写入或空间不足时，
操作返回失败，不会先确认奖励。Windows 原有存档路径、存档版本和奖励规则保持不变。

网页存档属于当前浏览器、用户配置和站点存储分区，不会自动与 Windows 同步。清理站点数据、
使用无痕模式或更换托管地址可能使存档不可用。请先 Export 并保留下载的备份。

页面要求支持 Web Locks 的现代桌面浏览器与 HTTPS；localhost 可用于本机测试。
同一存储分区只允许打开一个 Cat Quest 实例，避免多个标签页同时结算。

Focus 保留现有中断规则：程序循环停顿超过 10 秒会使会话无效且不发奖励。
请保持页面活动；睡眠、页面挂起和刷新不能当作已完成的专注时间。
End Early 仍按现有规则结算，Rest 不发奖励。

## 构建与上传

1. 使用 Unity **6000.3.10f1**，安装该版本的 **Web Build Support**，安装后重启 Editor。
2. 保存自己的场景编辑，选择 **Tools → Cat Quest → Build itch.io Web Version**。
3. 完成后使用 `Builds/Web-时间戳.zip`；状态记录在 `Temp/build-web.result`。
4. itch.io 项目类型选 **HTML**，上传 ZIP 并勾选 **This file will be played in the browser**。
5. 推荐点击后全屏启动，或设置 1280×720 嵌入尺寸并允许全屏。
6. 同页另放 Windows 下载包。在 itch.io 预览中检查存档、刷新、导出、键盘、缩放和声音后再发布。

构建工具会恢复先前的平台和被修改的 Web 设置，不重建页面、不改玩家数据、不自动上传。
构建和验证一次只运行一个。更多实现和检查说明见 [英文文档](web-release.md)。
