# Changelog

## v1.0.1

- 修复终末地绑定信息只处理 `defaultRole` 或 `roles[0]`、导致多角色遗漏的问题；现在会遍历并去重全部终末地角色。
- 调整终末地签到请求：使用 `sk-game-role: 3_<roleId>_<serverId>` 指定角色，并使用空的签名 POST body；当 `/api/v1` 明确返回 404/405 时才回退到 `/web/v1` 兼容路径。
- 修复修改每日签到时间后仅保存配置、Windows 计划任务仍保留旧时间的问题；现有任务会在保存设置时自动同步更新时间。
- 启动程序时读取 Windows 计划任务的实际触发时间，配置与实际任务不一致时给出明确提示。
- 统一程序集和发布文件名为 `SklandAutoSign.exe`，项目版本更新为 `1.0.1`。
- 新增 `build_release.ps1`，统一生成发布 EXE 和 `SHA256SUMS.txt`，减少手工改名或校验文件不一致的风险。
- 新增 `THIRD_PARTY_NOTICES.md`，补充第三方参考资料和许可边界说明。
