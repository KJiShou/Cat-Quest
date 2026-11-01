# 已批准的故事与教学音频

[English](README.md) | [简体中文](README.zh-CN.md)

`engine.cjs` 是 2026-10-31 从 [Babble Engine](https://babble.wrongfloor.studio/src/engine.js) 获取的未修改 1.4.0 原件，MIT 条款见 LICENSE.txt，SHA-256 记在生成目录。它在 Node 离线运行，不作为 Unity 可执行代码分发。

用户确认 B（Fern）用于主角，D（Lowell）用于 Master Loke。render.cjs 固定相同设置、中性情绪和 seed 31。WAV 为引擎干声导出，无浏览器回放混响，不增加处理或变调。

在仓库根目录运行：

```text
python Tools/DialogueAudio/generate.py
python Tools/DialogueAudio/generate.py --check
```

需要 Node 与 PyYAML。生成读取但不编辑场景和教学源码，通过实际角色引用解析对白，重复角色文本共用片段。教学从 C# 字面量提取，并显式处理组合的锁定错误消息。构造超出现有模式时更新提取并检查覆盖，验证不可自动运行生成。

保留已有 metadata，不自动删除过时音频，移除前检查引用。未知新文本沿用原打字时序且无声，直到显式重生成。名字奖励使用离线占位，不读取真实存档。
