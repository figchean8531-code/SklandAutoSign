# 森空岛游戏自动签到

面向 Windows 的森空岛签到工具，支持通过同一个森空岛账号为以下游戏执行每日签到：

- 《明日方舟》
- 《明日方舟：终末地》

程序直接访问森空岛相关接口，不启动安卓模拟器，也不使用截图识别、鼠标模拟或键盘控制。它提供图形化配置、立即测试、Windows 每日任务和本地日志功能。

> [!WARNING]
> 本项目是非官方工具，与鹰角网络、森空岛及相关游戏的运营方没有隶属或授权关系。第三方接口可能随时变化，自动化操作也可能受到服务条款或风控策略限制。使用者应自行判断并承担风险。

当前修正版：`v1.0.1`。版本变更见 [`CHANGELOG.md`](./CHANGELOG.md)。

## 功能

- 同时或分别签到《明日方舟》和《明日方舟：终末地》
- 自动读取森空岛账号中已绑定的角色
- 支持同一游戏下的多个绑定角色（终末地会逐个处理 `roles` 中的全部角色）
- 使用 Windows 任务计划程序每日定时运行，无需程序常驻
- 电脑错过执行时间后，在系统恢复且用户登录时补跑
- 将账号 Token 通过 Windows DPAPI 按当前用户加密保存
- 记录签到时间、结果和奖励信息，但不记录 Token
- 将“今日已经签到”视为成功结果，避免重复操作被误报为故障

## 运行环境

发行版适用于：

- Windows 10 或 Windows 11
- x64 处理器
- 能正常访问森空岛及鹰角账号服务的网络
- 一个已经在森空岛绑定相应游戏角色的账号

Release 中的单文件版本为自包含构建，不要求另外安装 .NET Runtime。项目目前不支持国际服、Linux 或 macOS。

## 下载与安装

1. 从 GitHub Releases 下载 `SklandAutoSign.exe`。
2. 将 EXE 放在稳定且有写入权限的目录，例如 `D:\Tools\SklandAutoSign`。
3. 不要直接从压缩包、浏览器临时目录或会被自动清理的目录运行。
4. 双击启动程序。

本项目未使用商业代码签名证书。Windows SmartScreen 可能在首次运行时显示未知发布者提示；如有疑虑，请检查源码并自行构建。

## 获取并保存 Token

程序不接收或保存手机号、密码和短信验证码。首次使用需要从已登录的森空岛网页取得 Token：

1. 在浏览器打开 <https://www.skland.com/>，完成账号登录和必要的验证码。
2. 保持登录状态，访问 <https://web-api.skland.com/account/info/hg>。
3. 页面会显示 JSON 数据。复制整个页面内容，或只复制 `data.content` 的值。
4. 将内容粘贴到程序的“账号 Token”输入框。
5. 勾选需要签到的游戏并选择每日运行时间。
6. 点击“立即测试签到”。确认结果正常后，再点击“启用每日任务”。

> [!CAUTION]
> Token 等同于敏感登录凭证。不要把 Token、截图、`data/settings.json` 或完整的 `data` 目录发送给他人，也不要提交到 GitHub。即使 `settings.json` 中的 Token 已加密，也不应公开。

## 定时任务行为

点击“启用每日任务”后，程序会创建名为 `森空岛游戏每日签到` 的当前用户级 Windows 计划任务。

- 程序无需保持打开，也无需添加到开机启动项。
- 到达设定时间时，任务会以 `--run` 参数静默启动同一个 EXE。
- 电脑休眠、关机或用户未登录导致错过时间时，会在条件恢复后补跑。
- 修改签到时间并点击“保存设置”时，如果每日任务已经存在，程序会自动同步更新 Windows 计划任务的触发时间。
- 程序启动时会读取计划任务中的实际触发时间；如果它与保存的配置不一致，会在状态栏提示。
- 移动或重命名 EXE 后，原任务中的路径会失效；请重新打开程序并点击“启用每日任务”。
- 点击“停用每日任务”只删除计划任务，不会自动删除本地 Token。

## 本地文件与隐私

程序会在 EXE 所在目录下创建 `data` 文件夹：

| 文件 | 用途 | 是否可公开 |
| --- | --- | --- |
| `data/settings.json` | 加密后的 Token、签到时间和游戏开关 | 否 |
| `data/sign.log` | 签到结果、角色昵称、渠道及奖励信息 | 不建议 |

Token 使用 Windows Data Protection API（DPAPI）的当前用户范围进行加密，通常只能在相应 Windows 用户上下文中解密。程序不包含遥测功能，也不会把 Token 发送到项目作者的服务器；Token 只用于鹰角账号授权和森空岛接口请求。

如需彻底清除本地数据，请先在程序中停用每日任务，关闭程序，然后删除 EXE 旁的 `data` 文件夹。

## 常见问题

### 提示“今日已经签到”

这表示相应角色当天已经领取签到奖励，程序会将其视为成功，不需要再次操作。

### 提示 HTTP 400 或其他接口错误

新版会尽量显示服务端 JSON 中的错误代码和消息。常见原因包括 Token 过期、接口协议更新、请求时间异常或服务端临时故障。先重新登录森空岛并更新 Token；如果仍然失败，可提交 Issue，并附上脱敏后的错误消息和日志。不要上传 Token 或 `settings.json`。

### 提示“未找到已绑定角色”

请确认对应游戏角色已经在森空岛中绑定，并确认程序中勾选的是正确游戏。

### 定时任务没有运行

依次检查：

1. EXE 是否仍位于创建任务时的原路径。
2. Windows 任务计划程序中是否存在 `森空岛游戏每日签到`。
3. 创建任务的 Windows 用户是否已经登录。
4. 电脑是否能够访问森空岛接口。
5. `data/sign.log` 中是否记录了失败原因。

## 从源码构建

需要安装 .NET 8 SDK。普通构建：

```powershell
dotnet build .\SklandAutoSign.csproj -c Release
```

生成 Windows x64 自包含单文件：

```powershell
dotnet publish .\SklandAutoSign.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\dist
```

输出文件为 `dist\SklandAutoSign.exe`。

### 发布 GitHub Release（维护者）

推荐使用仓库中的 `build_release.ps1` 统一构建 EXE 和 SHA256 文件：

```powershell
powershell -ExecutionPolicy Bypass -File .\build_release.ps1
```

脚本会在 `dist` 目录生成：

- `SklandAutoSign.exe`
- `SHA256SUMS.txt`

如果本机没有 .NET 8 SDK，也可以使用仓库自带的 GitHub Actions 工作流 `.github/workflows/build-release.yml`。提交到 `main` 后会自动构建 Windows x64 发布包，并在对应 Actions run 的 Artifacts 中提供下载。

发布时请按以下顺序操作：

1. 先把本次版本的源码修改提交到 `main`。
2. 确认仓库首页已经显示本次修改后，再从该最新提交创建版本 Tag，例如本次修正版 `v1.0.1`。
3. 上传同一次构建生成的 `SklandAutoSign.exe` 和 `SHA256SUMS.txt`。
4. 不要把 `data` 目录、`settings.json`、日志或任何真实 Token 打包进 Release。
5. 发布前至少完成一次《明日方舟》、终末地、多角色、重复签到和 Windows 定时任务测试。

> [!IMPORTANT]
> Release 的 Tag 决定 GitHub 自动生成的 `Source code (zip)` / `Source code (tar.gz)` 内容。必须先提交源码，再从该提交创建 Tag，避免 Release 二进制与自动生成的源码包不一致。

## 提交安全检查

仓库提供的 `.gitignore` 会排除本地配置、日志、构建缓存和发行文件。提交前仍建议运行：

```powershell
git status --short
git grep -n -i -E "token|password|secret|credential"
```

第二条命令也会匹配源码中的变量名和安全说明，应人工确认没有真实凭证值。不要只依赖自动扫描。

## 协议研究与参考资料

接口流程和兼容性研究参考了以下社区项目与资料：

- [devnakx/skyland_auto_checkin](https://github.com/devnakx/skyland_auto_checkin) — 明日方舟与终末地统一签到流程
- [sjtt2/endfield_auto_sign](https://github.com/sjtt2/endfield_auto_sign) — 终末地签到实现与国服接口配置
- [ProbiusOfficial/Skland_API](https://github.com/ProbiusOfficial/Skland_API) — 森空岛接口资料（已归档，部分内容可能过时）

这些项目不对本项目提供担保。第三方项目的代码、文档和其他内容仍受其各自许可证或版权条款约束；本项目的许可不会覆盖第三方权利。更详细的说明见 [`THIRD_PARTY_NOTICES.md`](./THIRD_PARTY_NOTICES.md)。

## 版权与使用许可

Copyright © 2026 Figchean8531.

本项目允许个人和组织在**非商业目的**下自由查看、使用、复制、修改和再分发本项目的代码及其修改版本，但应保留原作者的版权声明和本项目的许可说明。

**未经作者事先书面许可，不得将本项目或其修改版本用于商业用途。** 商业用途包括但不限于出售、收费服务、商业产品集成、付费部署、以营利为目的的再分发，以及其他直接或间接的商业获利行为。

本项目中引用、依赖或参考的第三方代码、资料、API 和其他内容，仍分别适用其原有许可证、版权声明及使用条款。本许可仅适用于作者有权进行许可的原创部分。

完整说明见仓库根目录的 [`LICENSE`](./LICENSE) 文件。需要商业授权时，请事先取得作者的书面许可。
## 反馈问题

提交 Issue 时建议提供：

- Windows 版本
- 程序版本或 Release 文件名
- 选择的游戏
- 脱敏后的完整错误消息
- 删除角色昵称、UID、Token 等信息后的相关日志

严禁在公开 Issue 中粘贴 Token、`settings.json`、手机号、密码、验证码或其他账号凭证。
