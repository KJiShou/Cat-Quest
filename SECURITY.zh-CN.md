# 安全政策

[English](SECURITY.md) | [简体中文](SECURITY.zh-CN.md)

## 支持范围

Cat Quest 是开发中的学生项目，安全修复针对当前 main 分支，旧版本和自行修改构建没有独立支持承诺，不保证回复修复时限，没有漏洞赏金计划。

相关问题包括凭证暴露、私密任务或 AI 数据泄露、未授权排行榜写入、不安全账号处理，以及影响主程序权威状态的无效伙伴请求。

## 私下报告

打开仓库 [Security Advisories](https://github.com/KJiShou/Cat-Quest/security/advisories)。若存在 Report a vulnerability，使用私密表单。若没有，只创建标题为 **Security contact request** 的 Issue 请求私人联系渠道，不公开漏洞细节、个人数据或概念验证载荷。

不要公开 token、凭证、OAuth 回调 URL、真实任务、完整存档或可利用细节。获得私人渠道后提供：

- 受影响版本、Windows/Unity 环境和功能。
- 影响及使用合成数据的最小复现。
- 预期与实际行为。
- 清理后的证据及可能缓解措施。

仅测试自己拥有或明确获授权的系统账号，不查询他人私密数据或在真实 Firebase 进行破坏性测试。公开披露前与维护者协调评估修复。

## 数据边界

主 Unity 和本地存档拥有进度权威，AI 和伙伴不发奖。排行榜只在用户选择参与后上传批准汇总，Chat 可向连接服务发送消息和请求的任务事实。本地任务 JSON 不加密，Windows 凭证与 DPAPI 保护具有独立范围。

配置遵循 [Firebase](Firebase/SETUP.zh-CN.md)及 [AI](AI_IMPLEMENTATION.zh-CN.md)指南，不嵌入服务账号密钥、管理凭证或真实 token。泄露后应向服务方撤销，删除公开文件评论本身不会使凭证失效。
