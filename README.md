# 微信工作台 · Windows / Android / 云端

这是 Visual Studio .NET MAUI 与 C# SDK 整合工程，同时保留 Python 控制台和旧 Kotlin 遥控端。手机独立版不需要连接电脑；云网页管理 Windows 节点时仍需要 Windows 代理在线。

## 手机 1.4

日常：启动微信助手 → 官方微信 → 旁边的悬浮工具球。

- 配置通用 OpenAI 兼容 AI 接口；密钥由本人在手机填写，安装包和源码不包含密钥。
- 收到微信新通知后生成预览，在系统提供快捷回复入口时可人工确认回复；没有入口时复制粘贴。
- 进入微信通讯录或朋友圈，在悬浮球点击“识别 / 选择好友”→“识别微信当前页”，勾选保存。手动翻页后可继续识别，不再输入作者名。
- 只识别可见辅助节点，不是完整通讯录接口；显示名不是 wxid，同名请设置不同备注，切换账号后清空重选。
- 朋友圈按所选好友辅助；自动巡检默认关闭，开启后约 15 秒检查一次，仅在解锁、朋友圈前台顶部及目标控件明确时尝试。不能保证别人发布后立即点赞。

**验证范围：** Android Release 构建、签名检查及 58 项离线规则检查通过。Android 构建有 1 项现有布局过时警告；Windows 构建有 2 项同类生成代码警告，均无错误。没有安卓真机连接；好友识别、悬浮球、通知回复和实际点赞待小米手机验证。电脑上展示的手机页面是 Windows MAUI 布局预览，不是运行 APK。

[手机安装与权限说明](build/maui/手机独立版使用说明.md) · [pyquan-like 源码解析](build/qa/pyquan-like源码解析.md) · [完整功能说明](README-升级版.md)

## 在 Visual Studio 中运行

安装 .NET 10 SDK 与 MAUI Windows / Android 工作负载。

- Windows：打开 `WeChatWorkspace.Windows.slnx`，将 MAUI Windows 工程设为启动项目，选择本地 Windows 计算机。
- Android：打开 `WeChatWorkspace.slnx`，选择 MAUI 工程的 Android 目标，连接本人手机或安装安卓模拟器。
- C# SDK 主机：`build/sdk/WeChatSdkHost`，依赖保留在 `build/sdk/vendor/WeChatAuto.SDK` 的源码。
- Python 控制台：`build/console`；旧 Kotlin 遥控工程：`build/android/WeChatLikeRemote`。
- CloudBase 部署源：`deploy/cloudbase`。云环境、服务地址和系统路径属于现有部署示例，使用自己的环境时需要调整；不包含登录资料。

```powershell
dotnet restore WeChatWorkspace.slnx
dotnet build build/maui/WeChatLikeMaui/WeChatLikeMaui.csproj -c Release -f net10.0-android
dotnet run --project build/qa/PhoneRulesTests/PhoneRulesTests.csproj -c Release
```

开发签名和已经安装的旧版是否一致，取决于构建电脑的签名环境。此源码仓库不上传签名私钥；在另一台电脑重新构建不保证能覆盖本项目已分发的 APK。

## 参考项目与许可

原有整合来源包括 WeChatAuto.SDK、WeChat-Auto-Like-System 和 wechatauto-replica。许可证副本见 [third-party-licenses](third-party-licenses)，保留的第三方源码按其自身许可使用；本仓库不通过统一声明替代第三方授权。

`Throokie/pyquan-like` 本轮仅静态解析，没有复制其源码或模板。它的 ADB / OpenCV 路线没有好友筛选，且不能直接成为独立手机插件。

## 合规声明

本工程整合自 GitHub 开源项目（MIT / Apache-2.0），仅供学习研究。通过界面自动化操作微信违反微信用户协议，频繁使用可能触发风控甚至封号；请勿用于骚扰、批量营销等违法违规用途。使用后果由使用者自行承担。
