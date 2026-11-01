# Security policy

[English](SECURITY.md) | [简体中文](SECURITY.zh-CN.md)

## Supported scope

Cat Quest is an actively developed student project. Security fixes target the current `main` branch. Older revisions and locally modified builds do not have a separate support commitment. There is no guaranteed response or remediation deadline and no bounty program.

Relevant reports include credential exposure, unintended disclosure of private task/AI data, unauthorized leaderboard writes, unsafe account handling, and invalid Companion requests that can affect authoritative local state.

## Report privately

Visit the repository's [Security Advisories](https://github.com/KJiShou/Cat-Quest/security/advisories) page. If **Report a vulnerability** is available, use that private form. If it is unavailable, open an issue titled **Security contact request** asking the maintainer for a private channel, with no vulnerability details, personal data or proof-of-concept payload.

Do not publish tokens, credentials, OAuth callback URLs, real task records, complete saves or exploitable details in a public issue. Once a private channel is available, include:

- Affected revision, Windows/Unity version and feature.
- Impact and minimal reproduction using synthetic data.
- Expected versus observed behavior.
- Sanitized evidence and any suggested mitigation.

Test only systems/accounts you own or have explicit authorization to test. Do not query other players' private data or perform destructive testing against a live Firebase project. Coordinate public disclosure with the maintainer so affected behavior can be assessed and corrected.

## Data boundaries

The main Unity application and local save own progress. AI and Companion are not reward authorities. Leaderboard uploads contain only approved aggregates and require opt-in. Chat can send messages and requested task facts to the connected AI service. Local task JSON is not encrypted. Windows credential/DPAPI protections have separate scope.

Use the procedures in [Firebase setup](Firebase/SETUP.md) and [AI implementation](AI_IMPLEMENTATION.md) for configuration. Never embed service-account keys, administrative credentials or real account tokens in the repository. Revoke exposed credentials through their provider. Deleting a public comment or file alone does not invalidate them.
