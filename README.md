# SSH Beacon

Windows 桌面上的入站 SSH 提示器。连接本机 22 端口时，屏幕角落出现暖白光点，光核固定，不对称光晕缓慢呼吸、旋转。最后一个连接断开后，光点用 6 秒淡出。监控异常时显示红点。

## 运行与预览

在 Windows 10/11 上用 .NET Framework 编译器构建，双击生成的 `SshBeacon.exe` 启动。

```powershell
.\build.ps1
.\SshBeacon.exe --preview
```

预览可看到一整圈旋转和断开淡出，按 L 切换深浅背景，空格重播。正式运行时，悬停光点可看来源和连接时长，拖动后会记住位置。首次运行会注册当前用户的开机自启，可在托盘菜单修改。

## 连接与访问日志

桌面光点每 800 毫秒检查一次入站 TCP 连接。托盘每 3 秒读取 `OpenSSH/Operational` 中的成功登录事件，短连接也能留下记录。同一地址、同一账号的相邻登录间隔在 60 秒以内时合并提示。

单击托盘图标或选择“查看访问日志”可清除未读角标，已读状态跨重启保留。启动时最多补读最近 7 天的 1000 次登录，窗口按时间倒序展示最近 200 组访问。补读记录通过角标提示，运行中新的一组访问另发系统通知。

## SFTP 收发记录

在管理员 PowerShell 中运行 `Enable-SftpHistory.ps1` 可启用原生 SFTP 的 INFO 日志。脚本会将默认或旧版 Beacon 子系统迁移到 `sftp-server.exe -l INFO`，在修改前备份配置，校验通过后重启 sshd。需要修改且有活动 SSH 会话时，等断开后再运行。恢复时使用 sshd 配置同目录的 `sshd_config.sshbeacon-*.bak`，核对后重启服务。

打开访问日志可查看当前 Windows 账号最近 7 天的收发记录，单击卡片可在资源管理器中定位本机文件。Beacon 按 SFTP 进程 ID 关联来源，按读写字节区分收发，零字节操作按打开模式显示请求方向。运行中的文件关闭事件也会触发未读提醒，同一批文件合并通知。传统 SCP（`scp -O`）和普通 SSH 命令写文件只留下登录记录。

历史版本在 `%LOCALAPPDATA%\SshBeacon\Transfers` 中的记录仍可读取。Windows 事件通道按容量滚动，原始路径可能保留超过 7 天。

## 项目约定

- 桌面状态和托盘入口在 `SshBeacon.cs`。调整光晕时改 `BeaconVisuals.cs`，正式光点与预览共用绘制逻辑，用原尺寸预览验收。
- 登录分组、SFTP 事件解析和日志窗口分别在 `AccessHistory.cs`、`SftpAudit.cs`、`ActivityWindow.cs`。文件传输由 Windows 原生 SFTP 服务处理，Beacon 读取日志。
- 交付时运行 `.\test.ps1` 和 `.\build.ps1`，给出用户能观察到结果的验收入口。行为变化写回本页。需要深浅背景对照图时运行 `.\test.ps1 -PreviewSheet`。
