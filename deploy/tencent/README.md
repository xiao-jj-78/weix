# 腾讯云 Windows 托管包

当前状态：本地构建和云端部署脚本已准备，**尚未连接腾讯云账号、创建付费资源或部署到任何实例**。需要明确的实例 ID/地域或公网 IP、Windows 版本及授权连接方式后，才能执行实际上传和安装。

## 托管范围

个人微信自动化依赖 Windows 上登录用户的图形桌面。腾讯云 Windows CVM 可以作为候选运行环境，但必须现场验证官方微信安装、扫码、UI Tree、远程桌面断开/锁屏后的行为；本项目没有保证锁屏或注销后继续自动操作。

Linux CVM、容器和无界面云函数可以承载 Web 服务或 AI 后端，不能直接运行本项目的 Windows UIA SDK 和个人微信扫码窗口。若现有实例为 Linux，应保留一台 Windows 工作节点，或另外准备 Windows 实例。

部署默认仅监听 `127.0.0.1:8765`，在云服务器自己的桌面浏览器访问。SDK 仅监听 `127.0.0.1:18766`。**当前局域网 API 没有公网用户鉴权，不应直接映射公网端口。** 如需手机跨公网访问，需先增加 HTTPS、用户认证或受控私有网络，再针对实际网络联调。

## 在目标 Windows 桌面安装

1. 通过腾讯云控制台远程登录目标 Windows 实例，使用将来实际操作微信的同一 Windows 用户。
2. 先检查 .NET 10 与 ASP.NET Core Runtime 10.x。原生 MAUI 可选；使用网页工作台时无需在云端启动 MAUI。
3. 将交付的 `微信工作台-三版本完整包.zip` 和本目录脚本复制到目标实例。
4. 在目标实例执行：

```powershell
./Install-TencentWindows.ps1 -PackageZip 'C:\Upload\微信工作台-三版本完整包.zip' -Destination 'C:\WeChatWorkspace'
```

5. 在已经登录的 Windows 桌面启动：

```powershell
C:\WeChatWorkspace\Run-CloudWorkspace.ps1
```

6. 打开 SDK 面板和官方微信，亲自扫码。先运行 SDK 兼容性诊断，再按需读取联系人；最后才选择要点赞/自动回复的用户。
7. AI 面板导入不含密钥的 Agnes 配置，重新填写密钥。当前电脑的 DPAPI 密文绑定当前 Windows 用户，不能当作云服务器的可用密钥备份直接复制。

安装脚本会检查路径穿越、交付文件 SHA-256 和目标目录是否为空，不会覆盖已有部署。升级前应停止旧服务并备份；本版不自动删除旧目录或改动官方微信。

可选：在同一 Windows 用户已登录的前提下，注册登录后启动任务：

```powershell
C:\WeChatWorkspace\Run-CloudWorkspace.ps1 -RegisterAtLogon
```

此任务不保存登录密码、不自动登录 Windows、不绕过微信扫码，也不保证锁屏后的 UI 自动化。

## 腾讯云自动化助手 TAT

TAT 可在已在线的腾讯云实例执行 PowerShell，适合解包、环境检查与文件部署。它不能替代交互式 Windows 桌面登录；不要从服务会话直接开启微信 UI 自动化。可以先用 TAT 准备文件，再在本人 RDP 桌面运行 `Run-CloudWorkspace.ps1`。

官方参考（2026-10-15 核查）：

- [腾讯云自动化助手简介](https://cloud.tencent.com/document/product/1340/52695)
- [TAT 与本地执行环境差异](https://cloud.tencent.com/document/product/1340/118990)
- [Windows 实例远程桌面登录](https://www.tencentcloud.com/document/product/213/32498)

## 当前仍需提供

目标实例 ID 与地域，或公网 IP 与操作系统；目前没有实例时，需要先确定预算和创建目标。不要将腾讯云 SecretKey 或 Windows 登录密码写进本部署目录、源码、截图或聊天。

## 合规声明

本工程整合自 GitHub 开源项目（MIT / Apache-2.0），**仅供学习研究**。通过界面自动化操作微信违反微信用户协议，频繁使用可能触发风控甚至封号；请勿用于骚扰、批量营销等违法违规用途。使用后果由使用者自行承担。
