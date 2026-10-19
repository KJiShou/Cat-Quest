# Focus 训练动画

共用 prefab：`Assets/Prefabs/Focus/FocusTrainingBattle.prefab`。Focus Scene、CharacterHome 的 Focus 页面及 Pet Widget 都使用它。根对象只有一个 Animator；两个子对象都是 SpriteRenderer，Acorn 已不需要 Spine 组件。

## 在 Unity 调整时机

1. 双击训练 prefab，选择根对象 `FocusTrainingBattle`。
2. 打开 **Window > Animation > Animation**（Ctrl+6），选择 `FocusTrainingLoop`，打开 Preview 后拖动时间轴。
3. 60 FPS 时间轴：Ninja 在第 12–41 帧挥刀（0.2–0.7 秒）；Acorn 在第 27–50 帧播放 Walk（0.45–0.85 秒），第 51 帧回 Idle。移动对应 Sprite 关键帧即可调整动作开始时间。Walk 是完整一次，24 帧；循环为 132 帧／2.2 秒。
4. `FocusIdle.anim` 是 4 秒待机循环，维持 Ninja 每秒一轮、Acorn 每 1.333 秒一轮的当前速度。

资源在 `Assets/Animations/Focus/`：`FocusTrainingLoop.anim`、`FocusIdle.anim`、`FocusTraining.controller` 及 `AcornFrames/`。每张 Acorn 帧为 512×512、透明背景、统一自定义 pivot 和 pixels-per-unit，朝向 Ninja 已烘焙进图片。不要另加 Ninja 子 Animator，也不需要 Animation Event。

## Focus 状态连接

`FocusTrainingBattle` 只把主程序／小窗注入的 Focus 状态写入 Animator 的 `IsTraining` Bool。Training 与 Idle 的双向过渡没有 Exit Time，Duration 为 0。Focus 时训练；Rest、取消、结束或小窗断线时待机。没有攻击计时、命中计数、手动采样、击飞或回位代码。独立 Focus Scene 的 `previewWithoutFocusSession` 可以用于直接 Play 预览。

## 重新烘焙

菜单 **Tools > Cat Quest > Animations > Bake Acorn and Generate Focus Clips** 会从 Layer Lab 原始 Acorn SkeletonData 烘焙 Idle／Walk，重建两个 clip 和控制器并更新共享 prefab。原始 Layer Lab 资源保持不变。此菜单会恢复本次默认时序，覆盖对生成动画的手动编辑；有自定义时序时先复制动画备份，再重新烘焙。

## 运行与验证

Windows：`Builds/Windows/CatQuest.exe`，发布时需保留同目录下所有构建文件。

验证结果与主页面／小窗截图在 `Library/FocusAnimationVerification/`。`clips.txt` 检查十轮共 1320 帧、一次 Walk、固定位置、接缝、动画长度及根 Animator 数量；Windows 的检查结果另外记录实际 Animator 状态、Sprite 变化、Focus/Rest/取消及小窗生命周期。
