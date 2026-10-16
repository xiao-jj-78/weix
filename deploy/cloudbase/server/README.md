# 微信工作台 CloudBase 桥接服务

入口为 `index.main`（`exports.main`），部署为独立普通事件云函数 `wechatWorkspaceBridge`，由 HTTP 访问服务挂载到 `/wechat-workspace-api`。HTTP/Web 云函数不是本版选择的运行类型。另保留 `node index.js` 作为本地 HTTP 诊断入口，默认仅监听 `127.0.0.1:8080`。

## 运行条件

- Node.js 18 或更高；依赖锁定 `tencentcloud-sdk-nodejs-tcb` 4.1.321，按 `package-lock.json` 安装。
- 生产队列仅使用现有 CloudBase PostgreSQL 的独立表 `wechat_workspace.queue`。使用受限的 `ExecutePGSql`，没有 NoSQL、进程内队列或失败回退。
- 先由部署人运行 `schema.sql`。它只创建独立 schema、表、索引并启用行级安全，撤销 PUBLIC/anon/authenticated 对这些新对象的访问；不改其他业务表或已有角色属性。运行数据库身份须拥有这张表或具备相应服务权限。
- `CLOUDBASE_ENV_ID` 为明确选定的现有环境；可选 `CLOUDBASE_PG_ROLE` 指定数据库角色，默认不传。`PUBLIC_ORIGIN` 必须设置为实际静态网站的完整 HTTPS 源，例如 `https://example.tcloudbaseapp.com`；浏览器跨域仅允许这个精确源。
- 普通事件函数通过 `TENCENTCLOUD_SECRETID`、`TENCENTCLOUD_SECRETKEY`、`TENCENTCLOUD_SESSIONTOKEN` 的运行时临时身份签名；每次数据库调用重新读取运行时值。缺失临时身份会明确失败，不读取本机 CLI 密钥、不要求长期 API Key。
- 服务器 `access-policy.json` 仅含不同的 `browserHash` 与 `agentHash` 两个 SHA-256 十六进制值。实际浏览器令牌和 Windows 令牌由部署流程另外生成及保存，不写入此说明、源码、网页或日志。
- 建议函数内存 256 MB、超时 120 秒。首次部署受控联调使用一个 Windows 节点，保持函数实例数/并发受控；ExecutePGSql 环境限额为 20 次/秒，单个热实例已统一限制为最多约 8 次/秒，浏览器等待结果默认间隔 1.2 秒。Windows 拉取默认立即返回，队列为空后客户端等待 3 秒再拉取，避免云函数在无命令时持续等待计费；如显式配置长轮询，内部检查间隔仍为 1.5 秒。此限速不是跨多个实例的全局限额。

## 协议

- 浏览器使用 `Authorization: Bearer <browserToken>` 调用固定 `/api/*` 路径。`GET /api/cloud/status` 直接返回 `{ok,nodeOnline,lastSeen,agentId}`；其余受限接口持久入队并等待 Windows 返回，沿用原 Python API 的 HTTP 状态与 JSON 正文。
- 所有浏览器 POST 必须携带 `X-Idempotency-Key`（16–128 个字母、数字、`_`、`-`）。一次操作的网络重试必须复用原值；相同值但不同正文返回 409。页面不应自行重试写操作。
- Windows 使用独立 agent 令牌，`POST /bridge/poll {agentId}` 尝试消费一次后立即返回 `{ok:true,command:null|{id,method,path,body,expiresAt,claimToken}}`。空结果后客户端等待 3 秒再试；必须与此行为兼容的 Windows 连接器一起发布。`expiresAt` 为 Unix 毫秒。测试或明确配置的调用方仍可指定 `pollMs` 使用长轮询。
- Windows 执行前必须重新检查有效期，并持久记录消费标识。`POST /bridge/result {id,status,body,agentId?,claimToken?}` 回传；后两个字段提供时会严格核对，重复相同结果可安全确认，不同结果返回 409。
- 浏览器角色不能拉取/回传命令；Windows 角色不能发起浏览器操作。节点心跳超过 45 秒即离线，不新增转发命令。每个环境同时只允许一个活跃 `agentId`。

普通命令有效期 30 秒；通讯录、插件运行、AI 预览为 110 秒。持久状态以版本比较交换进行原子更新：`queued → claimed` 只成功一次，绝不因租约/断线自动重新派发。所有修改以 `WITH changed AS (... RETURNING id) SELECT id FROM changed` 结束，兼容 ExecutePGSql 仅为最终 SELECT 保留行数据的实际行为，不把 `AffectedRows` 当作并发消费结果。排队过期命令不再消费；已消费但结果超时会显示“执行状态未确认”。已消费命令的迟到结果仍可写回，保留同一 ID，减少重复执行风险；没有宣称网络与外部微信操作能形成恰好一次事务。

浏览器请求上限 64 KB；Windows 回传 JSON 对象上限 512 KB（传输封套额外允许 4 KB）。任务仅允许固定五种、插件仅允许固定四种，任务自定义参数被拒绝。不支持任意 URL、任意脚本、控制台退出或 SDK 内部宿主退出。

命令被消费后立即从数据库清除请求正文。结果正文保留约 2 分钟，幂等标识和摘要保留 24 小时；Windows 轮询触发数据库清理，停机期间的数据会在下次连接时清理。长期无 Windows 连接时不是后台精确计时删除机制。

## 验证

`npm test` 仅使用明确的本地测试 store 和 PostgreSQL API 契约模拟器，不连接腾讯云、真实微信或 AI。覆盖角色隔离、白名单、大小限制、离线、幂等、并发消费、过期、迟到结果、TTL、事件/HTTP 适配、精确 CORS、PG CAS 与数据库失败关闭。实际部署和运行时身份仍由主部署流程单独验证。

服务器故障日志仅包含 `event`、固定 `stage` 与固定 `failureCode` 三个字段，用于区分队列读取、CAS、清理、限流与权限故障；不输出错误原文、调用栈、SQL、记录 ID、请求正文、联系人、密钥或运行时环境。

官方接口依据：[ExecutePGSql 参数和限额](https://cloud.tencent.com/document/product/876/130469)、[腾讯云 Node.js SDK](https://github.com/TencentCloud/tencentcloud-sdk-nodejs)。

本工程整合自 GitHub 开源项目（MIT / Apache-2.0），仅供学习研究。通过界面自动化操作微信违反微信用户协议，频繁使用可能触发风控甚至封号；请勿用于骚扰、批量营销等违法违规用途。使用后果由使用者自行承担。
