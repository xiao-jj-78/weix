# 腾讯云 CloudBase 版

此版本适配已选的上海云开发环境，使用现有标准版资源。网页托管在独立的 `wechat-workspace/` 路径，桥接函数名为 `wechatWorkspaceBridge`，网关路径为 `/wechat-workspace-api`。部署实际结果见 `deployment-status.json`，不能仅凭本说明判断已上线。

## 工作方式

云网页 → 经访问码验证的云桥 → 电脑主动连接 → 本机微信工作台与 C# SDK。

云端承载网页与转发服务，微信及其扫码窗口、界面操作和 AI 自动回复进程运行在 Windows 节点。电脑需要保持开机，微信登录且桌面能正常交互。现有云开发容器环境不能直接替代 Windows 微信桌面；需要整套服务在云端持续运行时，还需另外验证 Windows 云节点。

本机服务不需要开放公网端口。浏览器和电脑使用不同的随机访问码，电脑配置由当前 Windows 用户 DPAPI 加密。部署代码只含两个码的 SHA-256 值；源码包不含本机访问码、Agnes 密钥或腾讯云密钥。

云端使用独立 PostgreSQL schema `wechat_workspace` 保存短期队列与结果，启用行级安全且不开放给浏览器数据库身份。写命令需幂等标识，原子消费后不再次派发；断线时只回传已有结果。若页面显示“执行状态未知”，先查看电脑状态，再决定是否重新操作。

## 本机连接

1. 启动交付包的“后台托管版”，确认本机工作台可连接。
2. 运行 `windows/Start-CloudBridge.ps1`。
3. 运行 `windows/Copy-CloudBrowserToken.ps1`，将复制到剪贴板的访问码粘贴至云网页。复制窗口关闭或超时后会清理自己设置的剪贴板内容。
4. 需要退出云连接时运行 `windows/Stop-CloudBridge.ps1`，其不会退出官方微信。

访问码绑定本机已加密配置。复制交付包到其他电脑时需要重新配对，不能靠复制 DPAPI 密文完成登录。不要删除本机执行记录再重连旧队列。

## 开发与部署

`server/` 是普通事件云函数，入口为 `index.main`；通过事件运行时的短期身份访问选定环境 PostgreSQL，不读取本机 CLI 凭据，不注入长期账号密钥。`schema.sql` 只创建本项目的 schema、表和索引，不更改现有业务表或角色属性。

部署者先核对环境、已有函数和网关路径，完成本地测试，再创建独立桥接数据库结构。用部署工具在内存中产生两份随机令牌，本机保存 DPAPI 密文，上传暂存目录只保存 SHA-256 策略。之后部署指定函数与路由，将 `public/cloud-config.js` 的 `API_BASE` 填为实际验证通过的网关地址，再把 `public/` 上传到独立托管路径。不要直接把源码根目录或本机用户数据上传。

环境套餐、超额策略和运行时资源点遵从腾讯云账号设置，本工具不购买套餐、续费或修改扣费策略。网页访问与云函数调用会消耗已有套餐的资源。

官方参考（2026-10-15 核查）：[CloudBase CLI](https://docs.cloudbase.net/cli-v1/quick-start)、[普通云函数与 HTTP 网关](https://docs.cloudbase.net/cloud-function/quick-start)、[PostgreSQL SQL 接口](https://cloud.tencent.com/document/api/876/130469)。

## 合规声明

本工程整合自 GitHub 开源项目（MIT / Apache-2.0），**仅供学习研究**。通过界面自动化操作微信违反微信用户协议，频繁使用可能触发风控甚至封号；请勿用于骚扰、批量营销等违法违规用途。使用后果由使用者自行承担。
