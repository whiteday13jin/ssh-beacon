# SSH Beacon

Windows 桌面小组件：有远程 SSH 会话连入这台电脑时，屏幕角落出现一个置顶的闪烁小黄点；没有连接时完全隐藏。

## 功能

- 入站 SSH 连接时出现，断开 6 秒后淡出
- 悬停显示来源设备与连接时长
- 可拖动，位置持久化，重启后沿用
- 托盘常驻：开机自启、重置位置、打开日志、退出
- 监控异常时显示红点

## 构建

需要 .NET Framework 4.8（Windows 10/11 自带），用系统自带的 csc 编译：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

## 使用

双击 `SshBeacon.exe` 运行，首次运行会注册开机自启（HKCU Run）。右键小点或托盘图标可修改设置。

## 检测方式

每 800ms 读取一次本机 TCP 连接表，匹配 22 端口上处于 `Established` 的入站连接（含 Tailscale `100.64.0.0/10` 网段）。没有会话时不渲染、不占 CPU；检测失败时显示红点，避免误报"安全"。

## 文件

- `SshBeacon.cs`：源码
- `build.ps1`：编译脚本