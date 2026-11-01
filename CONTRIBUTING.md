# Contributing to Cat Quest

[English](CONTRIBUTING.md) | [简体中文](CONTRIBUTING.zh-CN.md)

Cat Quest is a single-student Unity/C# final-year project. Small, focused bug reports and suggestions are welcome. For substantial code contributions, discuss scope and permission with the maintainer first: the project is **all rights reserved**, not open source. Submitting feedback does not grant rights to redistribute project or vendor assets.

## Before starting

Read [README.md](README.md), [AGENTS.md](AGENTS.md), the affected feature document and the current implementation. Use Unity **6000.3.10f1** and the checked-in package configuration. Inspect Git status and preserve unrelated changes. Do not reset player data, change dependencies or rebuild an authored page as incidental cleanup.

Open an issue describing the concrete problem, proposed scope and observable result before a large change. Questions and reports may be written in English or Chinese. Do not attach personal task records, account tokens or full player saves. Use synthetic reproductions. Report security issues through [SECURITY.md](SECURITY.md).

## Making a focused change

1. Work on a separate branch and keep each change reviewable.
2. Inspect callers, serialized bindings and related state transitions before editing.
3. Keep business rules in existing services and presentation/input in UI components.
4. Preserve Unity `.meta` files and GUIDs. Move assets together with their `.meta` files.
5. Preserve manual prefab edits. Modify the requested subtree/properties, not the entire page. Never use a legacy whole-page generator to update existing UI.
6. Reuse the actual SimpleCasual reference components and shared project feedback components. Do not edit vendor originals.
7. Inspect the full diff and run `git diff --check`.

Never include `Library`, `Temp`, builds, logs, generated solution files, real saves or credentials in a pull request. Documentation images belong under `docs/images`. Raw verification evidence belongs in ignored output directories. Retain all third-party notices and obtain any necessary permissions before adding new assets.

## Verification proportional to the change

| Change | Expected evidence |
| --- | --- |
| Documentation | Verify paths, links, commands and feature claims against current code. Review the diff. |
| Cleanup | References, serialized bindings, affected behavior, missing scripts and actual Unity compilation when available. |
| UI | Actual before/after captures beside the selected vendor reference, ordinary 1920×1080 and 1280×720 layouts, interaction/motion and relevant states. Shared responsive changes also require 1024×768 and 2560×1080 with both sidebar states. |
| Rules/persistence | Narrowest relevant existing verifier. Success, failure/retry, duplicate calls, boundaries, reload and interruption as applicable. |
| Native Companion/build | Windows development player and actual companion-process lifecycle checks. |

Use one verifier at a time in the open Editor and isolated saves. Preserve/restore the prior environment, Play Mode start scene and Game View settings. Never use a real account or publish test aggregates as an incidental test. Do not weaken assertions to hide failures. Record current run times and distinguish current evidence from historical reports.

## Pull requests

Use the PR template to explain the problem, resulting behavior, affected files, actual validation and remaining gaps. For UI changes include sanitized before/after images and identify the exact reference component. For reward/persistence changes explain how duplicate settlement and save failures are handled. Report pre-existing failures separately.

Do not claim compilation, visual review or a Windows smoke pass unless performed on the submitted revision. Review is best-effort. Large architectural rewrites and unrequested feature expansion may be declined.
