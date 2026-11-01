# README and community-file design notes

[English](README_RESEARCH.md) | [简体中文](README_RESEARCH.zh-CN.md)

Prepared on 2026-10-31 for Cat Quest. The public README and project guides now have paired English and Simplified Chinese editions. It is structured for a project examiner or new developer: understand the product, see it, run it, then inspect implementation and limits.

## Sources reviewed

- [GitHub: About READMEs](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes) — purpose, usefulness, getting started, help and maintenance.
- [GitHub: Markdown formatting](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax) — relative repository links/images and descriptive image text.
- [GitHub: Repository best practices](https://docs.github.com/en/repositories/creating-and-managing-repositories/best-practices-for-repositories) — repository documentation and contribution expectations.
- [GitHub: Adding a security policy](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/configure-vulnerability-reporting/add-security-policy) — supported scope and vulnerability reporting instructions.
- [GitHub: Private reporting](https://docs.github.com/en/enterprise-cloud@latest/code-security/how-tos/report-and-fix-vulnerabilities/report-privately) — private reporting is a separate repository setting. Document a fallback when unavailable.
- [GitHub: Form schema](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-githubs-form-schema) — typed fields and required inputs for issue forms.

## Application to this project

| Reader question | README treatment |
| --- | --- |
| What is it and why would I use it? | Product introduction, intended audience and productivity loop. No invented research outcomes. |
| What actually exists? | Feature inventory checked against scripts, prefabs, build scenes and current feature documents. Optional online dependencies identified. |
| What does it look like? | Real rendered images with synthetic data and capture provenance. |
| How do I start? | Exact checked-in Unity version, Bootstrap/CharacterHome scenes, source setup and Windows build steps. |
| Why is a feature locked? | Current feature levels, boss gates and equipment prerequisites. |
| Where does data go? | Local save/backup, AI data sharing and the leaderboard allowlist. No cloud-save claim. |
| How is it structured and tested? | Small architecture diagram, source map and real verifier entrypoints. No fabricated CI/test badges. |
| Can I reuse or contribute? | User-selected all-rights-reserved notice, separate vendor notices and contribution/reporting routes. |

The README deliberately avoids unsupported minimum hardware specifications, a download button without a verified release, guaranteed online access, and claims that historical checks validate the current revision. The broader feature documents are linked rather than copied wholesale because some retain superseded notes.

## Community checklist mapping

Description is repository metadata. README, Code of Conduct, Contributing, License and Security policy live at the repository root. Issue forms and the PR template live in `.github`. GitHub must receive these files on its default branch before its remote checklist can reflect them. A custom reserved-rights notice is not an OSI open-source license and may not receive a standard GitHub license badge.
