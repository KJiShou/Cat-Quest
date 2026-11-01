# 应用光标

[English](CURSOR_SYSTEM.md) | [简体中文](CURSOR_SYSTEM.zh-CN.md)

主 Unity 应用在首场景前安装一个持久 AppCursor，使用 Cursor.SetCursor，在支持时为硬件光标，不加 UI 叠层，不改变按钮、导航、奖励或存档。独立 `--companion` 进程保留原生缩放光标。

## 图形与替换

在 Inspector 选择 `Assets/Resources/AppCursorSettings.asset`：

- **Normal** 为默认纹理，空值恢复系统光标。
- **Hotspot** 是从导入纹理左上角量起的像素点击点。
- **Button Hover / Button Hover Hotspot** 为有效、可交互 UI Button 的可选手形，空值保留 Normal。禁用或被射线阻挡按钮保持普通光标，即使鼠标不动而 UI 在下面关闭变化，也恢复正确状态。
- **Pressed / Pressed Hotspot** 是可选左键按下图与点击点，留空保持当前普通或悬停光标。替换图应对齐指尖，避免视觉跳动。

PNG 以 Cursor 导入，启用 Read/Write，禁用 mipmaps 和压缩，先用 Max Size 32，像素与 Canvas 缩放无关。赋值后将热点对齐可见尖端，保留现有 Resources 路径，不需改场景或运行生成器。

当前使用 Kenney Cursor Pack 1.1 的 `PNG/Outline/Default/pointer_toon_a.png`，原样复制到 `Assets/Resources/Cursors/KenneyToonArrow.png`。它是 32×32 黑边白色卡通箭头，可读、无压缩和 mipmaps，热点 `(3, 3)`。Pressed 空，因此点击不跳动。

悬停手为同包 `PNG/Outline/Default/hand_point.png`，复制到 `Assets/Resources/Cursors/KenneyHandPoint.png`，指尖热点 `(9, 3)`，导入参数相同。AppCursor 复用射线列表，每帧查看 EventSystem 顶层命中及点击处理层级，不给按钮加监听或组件，不干扰 JuicyButton。仅键盘选择不改鼠标形状，滑条、开关、输入框仍箭头，除非真实处理者是 Button。

来源为 [Kenney Cursor Pack](https://opengameart.org/content/cursor-pack-1)，CC0。原许可在 `Assets/Resources/Cursors/Kenney-License.txt`。这是用户选择的替代图，旧 SimpleCasualHand.png 及原包不改。之前参考为 `Prefabs/Prefabs_Component_UI_Etc/Tutorial_Focus_Icon_Hand.prefab`，换光标未改页面布局字体或按钮反馈。

应用失焦、指针出客户区、锁光标、组件禁用和退出恢复系统光标，不改 visibility/locking、时间比例、选择或指针事件。

## 手动视觉检查

在 Play Mode 的 1920×1080 和 1280×720 检查尖端对齐小按钮。点击拖滑条、输入、键盘导航、开关弹窗、换场景及 Alt-Tab。普通 ScreenCapture 不含硬件光标，需实际观察 Game View 和 Windows 指针后才能确认外观。伙伴缩放边缘继续使用方向光标。
