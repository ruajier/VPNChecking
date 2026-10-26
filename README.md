# Network Status

A lightweight Windows app that monitors network connectivity and local VPN services, displays your local IP, and alerts you to disconnections. Ideal for people who work with a VPN for extended periods.

Built with C# and WinForms. No cloud services or third-party NuGet dependencies required.

## Features

- Network status monitoring using Windows connectivity information.
- Local VPN monitoring through proxy processes, listening ports, or a selected VPN adapter.
- Local IP address and network adapter display.
- Alerts after two consecutive failed checks.
- Adjustable check interval and alert cooldown.
- System tray support and optional startup after sign-in.
- Light/dark themes and English/Chinese interface.

## Getting Started

Requires Windows 10 or 11 with .NET Framework 4.8.

1. Extract the portable package.
2. Run `网络状态.exe`.
3. Open **Settings** to configure monitoring.

Default settings:

| Setting | Default |
|---|---|
| Check interval | 10 seconds |
| Alert cooldown | 5 minutes |
| Local proxy port | 7897 |
| Start at sign-in | Disabled |

Use **Detect** in Settings to identify a running local proxy, or enter its port manually.

## Alerts and Closing

- **Click elsewhere on the screen:** dismiss the alert and start the cooldown. Monitoring continues.
- **Click “Stop and exit” or the alert’s ×:** stop monitoring and exit the application.
- **Click the main window’s ×:** hide the application to the system tray. Monitoring continues.
- **Double-click the tray icon:** reopen the main window.
- **Select Exit from the tray menu:** close the application completely.

If the connection is still unavailable after the cooldown, another alert will appear on a subsequent check.

**Test alert** follows the same dismissal and exit rules.

## Monitoring Scope

The app checks Windows network status and local VPN services without sending external probes. It displays your local LAN IP address; remote VPN server availability is not checked.

## Appearance

Use the two small buttons in the bottom-right corner:

- **◐:** switch between light and dark themes.
- **EN / 中:** switch between English and Chinese.

Your preferences are saved automatically.

## Settings and Startup

Settings are stored locally:

```text
%LOCALAPPDATA%\NetWatch\settings.json
```

Startup is disabled by default. When enabled, the app starts quietly in the tray after sign-in and waits for Windows to report an Internet connection before monitoring.

If you move the executable, disable and re-enable the startup option to update its path.

## Screen Click Handling

Screen-wide mouse click detection is active only while an alert is displayed and is removed when the alert closes. Click positions are not recorded or uploaded, and clicks continue to reach their original target.

## Build from Source

Run the following command in PowerShell from the repository root:

```powershell
.\build.ps1
```

The script uses the Windows .NET Framework C# compiler and produces `NetWatch.exe`. No dependency downloads are required.

## Project Structure

- `src/Core.cs` — network checks, IP selection, cooldown logic, and settings.
- `src/Program.cs` — interface, alerts, system tray, and screen click handling.
- `src/Appearance.cs` — interface translations.
- `tests/` — monitoring, IP, appearance, layout, and lifecycle tests.

## License

See [LICENSE](LICENSE) for licensing information.

---
# 网络状态 / Network Status

轻量 Windows 本地网络与 VPN 状态提醒工具。使用 C#、WinForms 和 .NET Framework 4.8，无需云服务或第三方 NuGet 依赖。

## 功能

- 读取 Windows 网络连通状态。
- 检查本地代理进程及监听端口，或指定 VPN 网卡状态。
- 显示活动 Wi-Fi / 有线网卡的本地 IP。
- 连续两次异常后提醒；点击屏幕其他区域关闭提醒，进入可设置冷却期，默认 5 分钟。
- 提醒弹窗“手动关闭，退出检测”按钮及其 ×：停止检测并彻底退出。
- 主窗口 ×：隐藏到托盘；托盘菜单可退出。
- 黑白主题、中英文切换；可选当前用户登录后等待联网再监测，默认不自启。

## 检测范围

本工具通过 Windows 网络状态和本地代理服务检测掉线，无需外部探测。显示本机局域网 IP；VPN 远端节点的可用性暂不检测。代理端口可在设置中修改或自动识别。

## 离线构建

在 Windows 10 / 11 上安装或启用 .NET Framework 4.8 开发环境后，在 PowerShell 执行：

```powershell
.\build.ps1
```

脚本调用系统自带的 .NET Framework 4.x C# 编译器；不联网恢复依赖。产物为本目录的 `NetWatch.exe`。

## 配置与自启

设置保存在 `%LOCALAPPDATA%\NetWatch\settings.json`。
开机启动仅在用户明确勾选并保存时配置 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `NetWatchLocal` 项。
更改 EXE 位置后应重新保存开机启动选项。

## 屏幕点击监听

只在提醒显示期间使用 Windows `WH_MOUSE_LL` 监听鼠标按下；关闭提醒后解除。
不记录或上传点击位置；原始点击继续传递给其他窗口。弹窗退出按钮和 × 被排除于暂时关闭逻辑之外。

## 源码与测试

- `src/Core.cs`：本机探测、IP 选择、冷却状态机和设置。
- `src/Program.cs`：WinForms 界面、提醒、托盘与短期屏幕点击监听。
- `src/Appearance.cs`：中英文文案。
- `tests/`：状态机、IP、布局、外观、点击分流及退出流程测试。

部分测试会短暂创建隐藏的 WinForms 控件；不调用界面接管工具。
`NativeHookTests` 会短暂注册原生鼠标监听来验证安装和释放，不注入鼠标事件。

## 发布状态与许可证

这是本地整理的源码发布候选包，尚未上传公开仓库。
目前尚未指定开源许可证；正式公开开源前，请由项目发布者选定并加入 `LICENSE`。
该包不包含个人配置、账号信息、运行日志、截图或测试输出。
