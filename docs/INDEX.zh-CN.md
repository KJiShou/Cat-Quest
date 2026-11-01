# 文档索引

[English](INDEX.md) | [简体中文](INDEX.zh-CN.md)

项目自有 Markdown 按英文与简体中文成对维护。英文保留原路径，中文使用同目录 `.zh-CN.md`，页面顶部可切换语言。路径、代码标识符、UI 标签、命令和正式素材名保留原文以便操作。

## 从这里开始

| 文档 | 英文 | 简体中文 |
| --- | --- | --- |
| `README.md` | [English](../README.md) | [简体中文](../README.zh-CN.md) |
| `docs/INDEX.md` | [English](INDEX.md) | [简体中文](INDEX.zh-CN.md) |
| `docs/LICENSING.md` | [English](LICENSING.md) | [简体中文](LICENSING.zh-CN.md) |

## 产品与实现指南

| 文档 | 英文 | 简体中文 |
| --- | --- | --- |
| `TASK_SYSTEM.md` | [English](../TASK_SYSTEM.md) | [简体中文](../TASK_SYSTEM.zh-CN.md) |
| `FOCUS_COMPANION.md` | [English](../FOCUS_COMPANION.md) | [简体中文](../FOCUS_COMPANION.zh-CN.md) |
| `FOCUS_ANIMATION.md` | [English](../FOCUS_ANIMATION.md) | [简体中文](../FOCUS_ANIMATION.zh-CN.md) |
| `BOSS_BATTLE.md` | [English](../BOSS_BATTLE.md) | [简体中文](../BOSS_BATTLE.zh-CN.md) |
| `EQUIPMENT.md` | [English](../EQUIPMENT.md) | [简体中文](../EQUIPMENT.zh-CN.md) |
| `AI_IMPLEMENTATION.md` | [English](../AI_IMPLEMENTATION.md) | [简体中文](../AI_IMPLEMENTATION.zh-CN.md) |
| `LEADERBOARD.md` | [English](../LEADERBOARD.md) | [简体中文](../LEADERBOARD.zh-CN.md) |
| `SETTINGS.md` | [English](../SETTINGS.md) | [简体中文](../SETTINGS.zh-CN.md) |
| `TUTORIAL_SYSTEM.md` | [English](../TUTORIAL_SYSTEM.md) | [简体中文](../TUTORIAL_SYSTEM.zh-CN.md) |
| `CURSOR_SYSTEM.md` | [English](../CURSOR_SYSTEM.md) | [简体中文](../CURSOR_SYSTEM.zh-CN.md) |
| `SOUND.md` | [English](../SOUND.md) | [简体中文](../SOUND.zh-CN.md) |
| `Firebase/SETUP.md` | [English](../Firebase/SETUP.md) | [简体中文](../Firebase/SETUP.zh-CN.md) |

## 编辑、设计与项目历史

| 文档 | 英文 | 简体中文 |
| --- | --- | --- |
| `AGENTS.md` | [English](../AGENTS.md) | [简体中文](../AGENTS.zh-CN.md) |
| `.impeccable.md` | [English](../.impeccable.md) | [简体中文](../.impeccable.zh-CN.md) |
| `PROJECT_REVIEW.md` | [English](../PROJECT_REVIEW.md) | [简体中文](../PROJECT_REVIEW.zh-CN.md) |
| `SOUND_PROMPTS.md` | [English](../SOUND_PROMPTS.md) | [简体中文](../SOUND_PROMPTS.zh-CN.md) |
| `Tools/DialogueAudio/README.md` | [English](../Tools/DialogueAudio/README.md) | [简体中文](../Tools/DialogueAudio/README.zh-CN.md) |
| `Assets/UI/Settings/README.md` | [English](../Assets/UI/Settings/README.md) | [简体中文](../Assets/UI/Settings/README.zh-CN.md) |

## 社区与文档维护

| 文档 | 英文 | 简体中文 |
| --- | --- | --- |
| `CONTRIBUTING.md` | [English](../CONTRIBUTING.md) | [简体中文](../CONTRIBUTING.zh-CN.md) |
| `CODE_OF_CONDUCT.md` | [English](../CODE_OF_CONDUCT.md) | [简体中文](../CODE_OF_CONDUCT.zh-CN.md) |
| `SECURITY.md` | [English](../SECURITY.md) | [简体中文](../SECURITY.zh-CN.md) |
| `THIRD_PARTY_NOTICES.md` | [English](../THIRD_PARTY_NOTICES.md) | [简体中文](../THIRD_PARTY_NOTICES.zh-CN.md) |
| `.github/pull_request_template.md` | [English](../.github/pull_request_template.md) | [简体中文](../.github/pull_request_template.zh-CN.md) |
| `docs/README_RESEARCH.md` | [English](README_RESEARCH.md) | [简体中文](README_RESEARCH.zh-CN.md) |
| `docs/images/README.md` | [English](images/README.md) | [简体中文](images/README.zh-CN.md) |

## 维护规则

1. 同一改动同时更新两种语言，保留相同规则、边界、数字、验证入口和历史限制，中文不是只含摘要的替代品。
2. 普通文档链接优先指向当前语言，语言切换可以跨语言，图片和代码路径共享。
3. 项目维护的 Markdown 不使用英文或中文分号，拆句或换行，不破坏可执行命令。
4. 保留现有英文路径，尤其 AGENTS.md、.impeccable.md、README.md 和 GitHub 默认文件，供工具正常发现。中文 PR 模板可通过顶部链接打开后复制使用，默认自动加载仍为英文模板。
5. 历史审查和声音设计保留明确历史标签，不把旧报告当本次验证。更改规则需先核对代码，不改变游戏来迁就文档。
6. 原始截图与测试产物留忽略目录，精选 README 图片放 docs/images，不因翻译重拍或更改 UI。

## 保留原文的范围

Assets/Spine/CHANGELOG.md 和 Assets/Spine/Runtime/spine-unity/Shaders/Sprite/README.md 是第三方包文档，保留原文，不套用本项目语言或标点约定。第三方许可证与源码声明也不改写。Library、Temp、Builds、Logs、obj、包缓存和生成目录不属于维护文档集。

根 LICENSE 不是 Markdown，它是英文正式保留权利声明，中文说明见授权说明，不创建竞争的法律文本。原供应商条款不因翻译而改变。

## 本次整理

整理日期为 2026-10-31。对照当前代码修正文档中的旧 Focus 上限、任务占位说法、Settings 实例方式和已替换音频来源，修复旧编码乱码。原规则、功能与作者 UI 不因文档整理改变。历史验证记录仍只证明其原运行时刻。
