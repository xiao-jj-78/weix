# C# SDK 桌面宿主 / 独立插件版

此工程直接引用附件 `WeChatAuto.SDK-master` 的 4.x C# SDK 源码构建，并为电脑控制台、网页和 MAUI 提供统一的本机接口。原始 `analysis` 未修改；`vendor/WeChatAuto.SDK` 保留上游 MIT 许可证。适配器源码、插件接口和示例插件均在本目录。

## 运行

需要 Windows x64 和 .NET 10 **ASP.NET Core Runtime**。当前开发电脑已安装 .NET SDK，可直接使用。

1. 运行电脑控制台，在 SDK 面板点击“启动宿主”；或运行 `start-sdk.ps1`。
2. 点击“打开微信登录”，在官方微信窗口亲自扫码/手机确认。宿主不接收微信密码、登录凭据或二维码内容。
3. 官方微信完成登录后，运行“读取 SDK 会话”；需要通讯录时，单独运行“读取通讯录姓名”。
4. “SDK 兼容性检查”和“桌面会话快照”不会操作微信界面，适合先检查连接。

宿主监听 `127.0.0.1:18766`，只通过现有控制台网关提供手机访问。停止宿主会停止 SDK 子进程，不会退出官方微信。

后台托管指宿主持续运行、监测客户端并为跨端请求提供服务。UIA 自动化仍依赖同一用户的交互桌面、已登录微信和公开的 UI Tree；它无法让个人微信在无桌面服务器、锁屏或注销用户后继续自动操作。网页或安卓端通过电脑网关调用，不在安卓上加载 Windows C# SDK。

## 已实现的两种 SDK 使用方式

- **后台宿主版**：后台每 5 秒检查官方进程；前台按需读取真实 SDK 账号、可见会话、通讯录姓名。
- **独立插件版**：宿主进程加载本地 `plugins/WeChatSdk.SamplePlugin.dll`，通过 `IReadOnlyPlugin` 执行桌面会话快照；还内置 3 个 SDK 插件。插件运行于独立宿主，不是注入微信的 DLL。

独立插件是本机受信任的 .NET 代码，接口本身并不构成安全沙箱。HTTP 只接受固定已安装的插件 ID，没有上传 DLL、指定程序集路径或运行任意命令的接口。要添加插件，应由项目所有者审阅并修改本地目录/加载清单后重新构建。

## SDK 适配细节

实际调用上游公开 API：

- `WeChatClientFactory.GetWeChatClientList()` 获取 SDK 验证的昵称、wxid 和进程。
- `client.Conversations.GetVisibleConversationTitles()` 获取当前可见会话标题，最多返回 100 条。
- `client.GetAllFriendNames()` 获取通讯录姓名，最多返回 500 条并给出原始数量/截断状态。上游会在本机写入自身通讯录缓存。
- `Win32WindowHelper.GetProcessId()` 读取官方窗口进程归属。

适配器通过公开依赖注入服务手动构造工厂，不调用会修改注册表的 `WeAutomation.Initialize()`。明确设置 `IsForceOpenUITree=false`，关闭 OCR、录屏、键鼠硬件模拟器、自动通讯录初始化；不执行 `ForceOpenUITree`、进程内存修改、注入、补丁和凭据/数据库读取。

两个按需 SDK 插件会按上游原有逻辑聚焦、调整微信窗口，切换页面，期间请避免同时运行其他鼠标自动化任务。它们只读取微信数据，不发消息、不点赞、不加好友。SDK 没有开放 UI Tree、微信未登录或版本不兼容时，界面应展示错误，不能当作已登录。

SDK 操作在隔离子进程里执行：会话读取最多 45 秒、通讯录最多 90 秒，超时或取消会终止子进程，避免上游无限重试阻塞主服务。被动状态始终保留 `login-unverified`，并标记最近一次主动读取时间，防止把进程存在误当成登录成功。

当前适配器实际支持附件 **4.x SDK 2.1.4**；3.9 SDK 仍保留在原始工程供兼容研究，未声称在此宿主完成适配。

## AI 供应商与智能回复

按照 [CC Switch](https://github.com/farion1231/cc-switch) 的供应商方案、当前方案切换、提示词管理方式增加了 AI 设置；没有复制其代码，也不会自动读取 CC Switch 本机数据库、登录信息或密钥。初始地址和模型留空，用户配置后才会请求 AI。

- 可保存多个命名方案，选择当前方案；支持 `openai-compatible`（Chat Completions）和 `ollama`（原生 `/api/chat`）。兼容地址可以填服务根地址、`/v1` 或完整 `/chat/completions` 地址，不会重复追加路径。远程服务使用 HTTPS；本机服务支持 HTTP。
- 密钥为只写字段：Windows DPAPI 当前用户加密保存，或使用方案指定的 `apiKeyEnv` 环境变量。读取配置、导出、状态、错误和宿主日志均不返回明文密钥。更换 Windows 用户后需重新填写密钥。空密钥字段保留现有值；`clearApiKey:true` 才会删除已保存密钥。
- 支持导入本项目的配置 JSON，或供应商单项 `settingsConfig.env` 中包含 `OPENAI_BASE_URL`、`OPENAI_MODEL`、`OPENAI_API_KEY` 的 JSON。不支持 CC Switch 整库备份、Codex TOML 或 Claude 专用协议；包含 `ANTHROPIC_*` 配置会明确拒绝，不能将它误当作 OpenAI 兼容接口。导出不包含密钥，迁移后需补填。
- 本机文本文件由界面读入 `systemPrompt`（最多 20,000 字符）；服务端不会按网页提供的任意路径读取文件。`preview` 只用用户输入的测试消息生成回复，不发送微信。

当前附件的 `MessageMonitor.cs` 只有构造器及空 `Dispose`，没有可调用的消息监听实现。因此智能回复使用 SDK 公开 `GetChatHistory`、`GetTitle`、`SearchFriend` 和 `SendMessage` 进行隔离进程轮询，不承诺原工程缺失的监听功能。

点击启动后，仅处理 **1–10 个所选私聊联系人**，多微信账号必须指定唯一账号名或 wxid。联系人需使用唯一备注名，不支持同名身份鉴别或群聊。先读取当天历史建立基线，不处理旧消息、自己发送的消息或已处理消息；SDK 时间只精确到分钟，因此启动当分钟的消息会保守忽略。每轮仅对最新一条新文本回复一次，默认至少间隔 15 秒、每小时最多 20 条、运行 60 分钟后停止。超过频率的消息不补发。

智能回复默认关闭，重启宿主不会自动恢复。运行时禁止更换配置并占用 SDK 操作锁，避免通讯录读取和回复相互抢占界面。每次发送前再次核对会话类型和完整名称；失败即停止。停止会终止本次隔离回复进程，宿主退出也会停止。SDK 连续 120 秒未响应会被终止。仍需官方微信已登录、桌面保持解锁，并避免人工或其他程序同时操作微信。

这部分已通过模拟 AI 服务和消息策略测试；未使用真实账号发送消息或调用用户付费模型，实际兼容性需要用户填写自己的接口后联调。

## API（JSON）

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/sdk/status` | 宿主运行、SDK 能力、客户端进程、最近主动读取的账号及插件 |
| POST | `/api/sdk/login` | 打开本机官方微信；body `{}` |
| GET | `/api/sdk/plugins` | 本机允许的插件目录 |
| POST | `/api/sdk/plugins/run` | body `{"id":"sdk-health"}`；也支持 `session-snapshot`、`sdk-session`、`sdk-contacts` |
| GET | `/api/sdk/contacts` | 主动读取通讯录；返回 `{ok,accounts,data:[{account,count,names,truncated}],readAt}`，仅在用户点击时调用 |
| GET/POST | `/api/sdk/ai/config` | `{ok,config:{activeProfileId,profiles,systemPrompt,allowedContacts,account,allowGroups,minReplySeconds,maxRepliesPerHour,sessionMinutes},running}`；POST 为部分字段更新 |
| GET | `/api/sdk/ai/profiles/export` | 与读取配置相同的脱敏 JSON，不包含明文或加密密钥 |
| POST | `/api/sdk/ai/profiles/import` | `{"json":"JSON 文本"}`，最多 200 KB |
| POST | `/api/sdk/ai/preview` | `{"message":"测试消息"}` → `{ok,reply,profileId,model,sentToWechat:false}`；最多等待 45 秒 |
| POST | `/api/sdk/ai/start` | `{}`，启动所选私聊联系人的智能回复；异步初始化，随后轮询状态 |
| POST | `/api/sdk/ai/stop` | `{}`，停止本次智能回复 |
| GET | `/api/sdk/ai/status` | `{ok,running,state,replyCount,startedAt,lastActivityAt,lastError,mode,autoResume:false}`，不包含聊天内容或密钥 |
| POST | `/api/sdk/host/stop` | `{}`，本机管理接口，响应结束后正常停止宿主；网关不向外透传此路径 |

供应商对象：`{id,name,protocol,baseUrl,model,apiKeyEnv,hasApiKey}`。POST 可增加 `apiKey` 或 `clearApiKey`；`profiles` 字段会替换方案列表，请提交保留的所有方案。未提交 `profiles` 时保留原列表。示例（将占位内容改为自己的服务设置后保存）：

```json
{
  "activeProfileId": "my-provider",
  "profiles": [{"id":"my-provider","name":"我的供应商","protocol":"openai-compatible","baseUrl":"https://your-provider.example/v1","model":"your-model","apiKeyEnv":"WLC_AI_API_KEY"}],
  "systemPrompt": "请礼貌、简洁地回复。",
  "allowedContacts": ["唯一联系人备注"],
  "account": "",
  "minReplySeconds": 15,
  "maxRepliesPerHour": 20,
  "sessionMinutes": 60
}
```

控制台网关额外提供 `/api/sdk/start`、`/api/sdk/stop`；由网关管理生命周期。所有写请求必须是 `application/json`；拒绝来自非本地网页 Origin 的直接写请求。无 CORS 开放配置。

自定义官方微信安装目录：启动前在本机设置 `WLC_WECHAT_EXE` 为实际的 `WeChat.exe` 或 `Weixin.exe` 路径。HTTP 无法修改此配置。

## 重建与验证

在 PowerShell 运行 `./publish.ps1`。需要分发到未装运行时的 Windows 时，可运行 `./publish.ps1 -SelfContained` 生成包含 .NET 的版本。初次恢复依赖会下载上游 OCR/ONNX/OpenCV 原生包，虽然当前适配器关闭 OCR，也保留完整上游依赖以确保程序集加载。

运行 `./smoke-test.ps1` 验证宿主启动、循环地址绑定、真实 SDK 程序集、示例插件加载、健康插件、未知插件错误和跨站写入保护。测试不会启动微信、读取联系人、发送消息或点赞。

运行 `publish/WeChatSdkHost.exe --ai-selftest` 进行 28 项模拟测试：协议地址、请求/响应、错误脱敏、DPAPI、方案导入切换、历史/自身消息/重复消息过滤。运行 `./ai-smoke-test.ps1` 验证真实宿主 HTTP 配置、脱敏导出、未配置拒绝和正常停止。二者都不访问真实 AI 或操作微信；测试配置使用独立临时目录。

真实扫码、4.x SDK 获取账号/会话和通讯录需要用户已登录的微信与兼容 UI Tree，未经过真实账号联调时不得宣称已经实测成功。

## 多微信窗口管理

新增 GET `/api/sdk/clients` 和 POST `/api/sdk/clients/open`、`/focus`、`/alias`、`/verify`，不更改既有接口。窗口列表被动发现同一 Windows 会话的官方客户端；`instanceId` 绑定 PID 与进程启动时间。focus 接收 instanceId，alias 接收 instanceId 和 alias（最多 60 字符）；open 与 verify 不接收参数。verify 通过现有 SDK 显式读取账号，返回上次验证时间，不据窗口存在断言已登录。open 请求官方启动器，不能强制多开。所有云端操作仍受固定路由、认证与防重复消费控制。

## 合规声明

本工程整合自 GitHub 开源项目（MIT / Apache-2.0），**仅供学习研究**。通过界面自动化操作微信违反微信用户协议，频繁使用可能触发风控甚至封号；请勿用于骚扰、批量营销等违法违规用途。使用后果由使用者自行承担。
