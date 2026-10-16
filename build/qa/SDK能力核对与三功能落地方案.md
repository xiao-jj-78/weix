# 微信工作台 · SDK 能力核对与三功能落地方案

> 任务 #78 产出。基于真实源码（`upgrade_src/build/sdk/`、`build/maui/WeChatLikeMaui/`）逐项核对，给出「一键登录 / 用户表+聊天 / 独立宿主插件」三功能的已实现度、差距与落地改动点。
> 合规边界沿用已确认结论：**独立宿主插件（现有架构，非微信进程内注入）；数据仅限本人已登录账号 / 本机本地只读；登录不自动化凭据输入。**

---

## A. 架构事实（带证据）

| 组件 | 事实 | 证据 |
|---|---|---|
| 宿主 `WeChatSdkHost` | .NET 10 `net10.0-windows` / `win-x64`，Kestrel **仅绑定 loopback 127.0.0.1**，`--port` 默认 **18766** | `WeChatSdkHost/Program.cs:23,30,35` |
| 宿主安全闸 | 仅 JSON、跨域 Origin 非 loopback 一律 403、请求体 ≤256KB、远程 DLL 上传/路径命名被拒绝 | `Program.cs:36-56`；`PluginCatalog.cs:11-13` |
| SDK 来源 | `WeChatAuto.SDK`（4.x 官方桌面客户端公共 UIA），别名 `Weixin` 进程；**仅 Windows 桌面微信** | `SdkWorker.cs:71-76`；`HostState.cs:96-110` |
| SDK 隔离 | 每次只读走**独立子进程** `--sdk-worker`，硬超时 45s/90s，超时即杀进程；不调用 `ForceOpenUITree`、不写注册表、不 patch 内存 | `SdkWorker.cs:13-15,57-89`；`HostState.cs:113-150` |
| 插件契约 | `IReadOnlyPlugin`：插件跑在**宿主进程内、绝不进微信**；只暴露 snapshot | `WeChatSdk.PluginContracts/IReadOnlyPlugin.cs:3-18` |
| 插件加载 | 仅从本地 `plugins/WeChatSdk.SamplePlugin.dll`（owner 安装的程序集）反射加载；远程 DLL 不可注入 | `PluginCatalog.cs:14-23` |
| MAUI 桥接 | `ConsoleApi` 走 HTTP；`SdkStatusAsync→/api/sdk/status`、`SdkReadAsync("contacts")→GET /api/sdk/contacts`、`SdkActionAsync("login",_)→POST /api/sdk/login` | `ConsoleApi.cs`（`Sdk*` 方法） |
| MAUI 本机启动 | Windows 上 `LocalConsoleHost` 启动同目录 `微信自动化控制台.exe` 连 127.0.0.1 | `LocalConsoleHost.cs:24-58` |
| MAUI 窗口伴随 | Windows 专属；在屏幕一侧排布本 App 与微信窗口 | `CompanionWindows.cs`（仅 `#if WINDOWS`） |

**关键约束**：WeChatAuto.SDK 只能驱动 **Windows 桌面微信 4.x**。Android 手机独立版**无法在本机跑 SDK**，必须经同一局域网让 Windows 宿主代跑，宿主返回 JSON。因此：
- 「一键登录」在手机上 = 指令 Windows 宿主打开官方微信登录（二维码出现在桌面）；
- 「用户表 / 聊天」在手机上 = 经 LAN 向宿主请求，宿主用 SDK 读取本人数据后回传。

---

## B. SDK 能力核对表（三功能 → 真实 API）

| 需求 | SDK 真实 API（vendor `WeChatAuto.SDK`） | 宿主是否已暴露 | 暴露位置 |
|---|---|---|---|
| **① 一键登录微信** | `HostState.OpenOfficialLogin()` / `ClientManager.Open()`：启动官方 `Weixin.exe`，复用既有登录会话，**用户本人扫码**；不自动填凭据 | ✅ 已暴露 | `POST /api/sdk/login`（`Program.cs:MapPost`）；`HostState.cs:88-118`；`ClientManager.cs:Open()` |
| **② 用户表（通讯录）** | `WeChatClient.GetAllFriendNames()` → `List<string>`；`GetAllFriends()` → `List<FriendInfo>`（含 wxid 等） | ✅ 已暴露（仅姓名） | `GET /api/sdk/contacts` → `SdkWorker` `contacts` 分支（`SdkWorker.cs:34-38`）；`HostState.RunSdkReadOnlyAsync("contacts")` |
| **② 聊天信息** | `WeChatClient.GetChatHistory(who, date)` / `(who, start, end)` → `List<ChatSimpleMessage>`；`ChatHeader.GetTitle()` 取当前会话标题 | ❌ **未暴露** | 需新增 `chat` 操作（见 D.3） |
| **③ 独立宿主插件** | 实现 `IReadOnlyPlugin`，宿主 `POST /api/sdk/plugins/run` 按 Id 调用 | ⚠️ 框架在、示例在，但**三功能插件尚未落地** | 需新增插件程序集（见 D.4） |

`WeChatClient` 关键只读成员（证据 `Components/WeChatClient.cs`）：
- `NickName`(51) / `WxId`(52) / `ClientProcessId`(49) — 登录账号识别
- `GetWeChatClientList()`（`WeChatClientFactory.cs:132`）— 登录态探测
- `GetAllFriendNames()`(831) / `GetAllFriends()`(824)
- `GetVisibleConversationTitles()`(346) / `GetVisibleConversations()`(353)
- `GetChatHistory(who, date)`(549) / `(who, start, end)`(559)

---

## C. 已具备 vs 待补

**已具备（可直接用，无需改宿主）**
1. 登录端点 `POST /api/sdk/login`（开官方微信 + 用户扫码）。
2. 状态端点 `GET /api/sdk/status`（含 `session`：`client-not-running` / `login-unverified` + 进程列表）。
3. 通讯录端点 `GET /api/sdk/contacts`（姓名，上限 500）。
4. 插件运行框架 `POST /api/sdk/plugins/run` + `IReadOnlyPlugin` 契约 + `PluginCatalog` 本地加载。
5. MAUI `ConsoleApi` 已封装 `SdkStatusAsync / SdkReadAsync / SdkActionAsync` 三件套。

**待补（实现三功能所需改动）**
- **D.1 一键登录 UI**：MAUI 新增「登录微信」按钮 → `SdkActionAsync("login", null)`；状态页读 `/api/sdk/status.session` 显示登录态。（小改动）
- **D.2 用户表 UI**：MAUI 新增「通讯录」页 → `SdkReadAsync("contacts")` 展示姓名列表 + 导出本地 CSV（透明、本人）。（小改动）
- **D.3 聊天信息**：宿主新增 `chat` 操作（带 `who` 参数）+ 端点 `GET /api/sdk/chat?who=`；`SdkWorker` 增加 `--who` 分支调用 `GetChatHistory`；MAUI 新增「聊天记录」页（先选会话→拉历史→本地导出）。（中改动，`HostState`+`SdkWorker`+`Program`+MAUI 四处）
- **D.4 独立宿主插件**：新建 `WeChatSdk.OwnDataPlugin` 程序集，实现 `IReadOnlyPlugin`，封装 `login`/`contacts`/`chat` 三个只读操作；`PluginCatalog` 注册；经 `POST /api/sdk/plugins/run` 调用。（中改动，验证插件机制）

---

## D. 落地方案（对应任务 #79–#82）

> 改动均保持「只读 / 本人 / 本机 / 不注入」原则；任何写操作、批量抓取、他人数据一律不做。

### D.1 一键登录（任务 #79）
- **宿主侧**：已就绪，无需改（或微调 `OpenOfficialLogin` 返回更明确的状态 JSON）。
- **MAUI 侧**：`MainPage.WechatEntry.cs` 新增按钮 → `ConsoleApi.SdkActionAsync("login", new {})`；状态区绑定 `SdkStatusAsync().session.state`。
- **边界**：仅打开官方客户端；凭据由用户在官方窗口扫码完成。

### D.2 用户表（任务 #80 之一）
- **宿主侧**：已就绪（`/api/sdk/contacts`，姓名）。可选增强 `GetAllFriends()` 回传 wxid（需扩 `SdkWorker` 的 `contacts` 分支序列化）。
- **MAUI 侧**：新增「通讯录」页，`SdkReadAsync("contacts")` → 列表；「导出」按钮写本地 CSV（路径告知用户，透明）。

### D.3 聊天信息（任务 #80 之二）⚠️ 主要新增
- **宿主 `HostState.RunSdkReadOnlyAsync`**：把 `operation is not ("session" or "contacts")` 改为亦接受 `"chat"`，从参数取 `who`；`SdkWorker` 增加 `--who` 参数与 `chat` 分支调用 `pair.Value.GetChatHistory(who, default)`。
- **宿主 `Program.cs`**：新增 `GET /api/sdk/chat`（`who` 来自 query，做长度/字符校验，拒绝路径与脚本）。
- **MAUI**：新增「聊天记录」页——先 `contacts`/`session` 选 `who` → `SdkReadAsync("chat?who=...")` → 时间线展示 → 本地导出。
- **边界**：仅本人已登录账号的可见会话；`who` 由用户从本人列表选择，不接受任意输入。

### D.4 独立宿主插件（任务 #81）
- 新建 `build/sdk/WeChatSdk.OwnDataPlugin/`（仿 `WeChatSdk.SamplePlugin`）：实现 `IReadOnlyPlugin`，`RunAsync` 内按 `context.RunSdkReadOnlyAsync(operation)` 复用宿主已有只读通道，operation ∈ {login, contacts, chat}。
- `PluginCatalog` 注册该插件 Id；`publish.ps1` 把 DLL 拷到 `plugins/`。
- 经 `POST /api/sdk/plugins/run {id, op}` 调用，验证「独立宿主、本地安装、非微信内」三原则。

### D.5 编译验证（任务 #82）
- 宿主：`dotnet publish -c Release -r win-x64`（`WeChatSdkHost.csproj`），产出 `微信自动化控制台.exe` 入 `deliverables/升级版-微信工作台/`。
- MAUI：`dotnet build WeChatLikeMaui.csproj -f net10.0-android -c Debug -p:AndroidPackageFormat=apk` → 复制签名 APK 到桌面（注意 **ASCII 路径**、显式指定 csproj，避 APT2265 / MSB1011）。
- 真机/桌面核对表沿用 `真机验证清单.md`，追加三项：登录按钮、通讯录页、聊天记录页。

---

## E. 合规边界重申（与已确认一致，实施中不得突破）
1. 仅驱动**本人已登录**的官方 Windows 桌面微信；不碰他人账号、不批量爬取、不做养号/群发/刷量。
2. **绝不**进行微信进程内注入、内存 patch、注册表篡改、远程 DLL 注入（`SdkWorker` 已明确禁用 `ForceOpenUITree` 与全局 `Initialize`）。
3. 登录**不自动化凭据**：只打开官方客户端，由用户本人扫码/确认。
4. 数据**透明、本地、只读**：导出文件落本机并告知用户路径；不外传、不上云、不做隐蔽抓取。
5. 插件**仅本地 owner 安装**程序集；宿主拒绝任何远程上传或路径命名。

---

## F. 待你确认后开工（见随附问题）
- 三功能实现优先级 / 是否一次全做；
- 平台侧重：Windows MAUI 直连优先，还是手机经 LAN relay 也要同步做；
- 聊天「导出」是否要（默认仅展示 + 可选本地 CSV）。

---

## G. 实施结果（2026-10-16 已完成，本文件转为「已落地记录」）

上文 D.1–D.5 已全部实施，并在实施中修正了两处偏差：

| 项 | 方案原写 | 实际落地 | 备注 |
|---|---|---|---|
| D.1 一键登录 UI | 放 `MainPage.WechatEntry.cs` | 集中在新页 `WeChatDataPage.xaml` 的 **一键登录微信** 按钮 → `ConsoleApi.SdkActionAsync("login", new{})` | 与 D.2/D.3 同页，减少跨文件分散 |
| D.2 通讯录/聊天 UI | 分「通讯录页」「聊天记录页」两页 | 合并为一页 `WeChatDataPage`，含**读取通讯录（用户表）**、`who` 输入框、**读取聊天记录**、**导出 CSV** | 一次连接即可完成全部只读动作 |
| D.3 聊天端点 | `GET /api/sdk/chat?who=` | 同上；`HostState.ValidateWho` 额外拒绝长度 > 96、控制字符、`/`、`\`、`..` | 网关侧再校验一次（双保险） |
| D.4 插件程序集 | 新建 `WeChatSdk.OwnDataPlugin` | 已建，Id=`own-data`，支持 `login` / `contacts` / `chat`；`publish.ps1` 已拷贝到 `plugins/` | 已加入 `WeChatWorkspace.slnx`（第 7 个工程） |
| D.4 插件契约 | 仅 `RunAsync(context, ct)` | 新增 `PluginRunArgs(Operation, Who)` 重载 + 默认实现（不破坏旧插件） | `own-data` 用重载；`session-snapshot` 等旧插件零改动 |
| D.5 编译验证 | `net10.0-android -c Debug` | 改为 **Release** 构建产物交付（Debug 包曾被误当交付物，已撤回） | 见 `验证记录.md`「三功能回迁与产物纠正」 |

宿主端点实测（重建后工作区宿主，`--port 18777`）：

| 请求 | 实测响应 |
|---|---|
| `GET /api/sdk/plugins` | `ok=true`、`errors=[]`，含 `own-data`（built-in）与 `session-snapshot`（local-assembly） |
| `GET /api/sdk/chat?who=filehelper` | `ok=false`、`error=sdk_read_failed`（无 UI Tree，**非 404**） |
| `GET /api/sdk/chat` | `ok=false`、`error=invalid_request`、`message=会话标识 who 不能为空。` |
| `GET /api/sdk/chat?who=..%2Fetc` | `ok=false`、`error=invalid_request`、`message=会话标识包含非法字符。` |
| `POST /api/sdk/plugins/run {id:"own-data",operation:"contacts"}` | 经插件路由，同上优雅失败 |
| `POST /api/sdk/plugins/run {id:"nope"}` | 404 `plugin_not_found` |

上游网关：控制台 `SdkBridge.ROUTES` 已放行 `/api/sdk/chat` 并透传 `query`；`verify_console_chat_route.py` **9/9 PASS**。
「微信数据」页：Windows MAUI 渲染器 + 桩网关运行时取证 **14/14 PASS**（截图 `界面预览/wechat-data-page.png`）。

**仍未做**：安卓真机、真实微信登录、真实会话读取、消息发送、点赞。真机步骤见 `真机验证清单.md` §J2。
