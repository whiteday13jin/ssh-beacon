# 在管理员 PowerShell 中运行。保留现有 SSH 登录配置，仅调整默认 SFTP 子系统。
param([string]$AccountSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:ProgramData 'ssh'
$config = Join-Path $base 'sshd_config'
$server = Join-Path $env:SystemRoot 'System32\OpenSSH\sshd.exe'
$install = Join-Path $env:ProgramFiles 'SshBeacon'
$helper = Join-Path $install 'SftpAudit.exe'
$source = Join-Path $PSScriptRoot 'SftpAudit.exe'
$account = (New-Object Security.Principal.SecurityIdentifier($AccountSid)).Translate([Security.Principal.NTAccount]).Value
# sshd 的子系统路径使用短目录名，避免旧版 Windows OpenSSH 对空格的处理差异。
$short = (New-Object -ComObject Scripting.FileSystemObject).GetFolder($env:ProgramFiles).ShortPath
$command = ((Join-Path $short 'SshBeacon\SftpAudit.exe') -replace '\\','/') + ' --account ' + $AccountSid
if ($command.Split('/')[0] -match '\s' -or $short -match '\s') { throw '此安装路径需要可用的短目录名。' }
$line = "Subsystem sftp $command"
$old = [IO.File]::ReadAllText($config)
$default = '(?im)^[ \t]*Subsystem[ \t]+sftp[ \t]+sftp-server\.exe[ \t]*(?=\r?$)'
if ($old.Contains($line)) { $next = $old }
elseif ([regex]::Matches($old,$default).Count -eq 1) { $next = [regex]::Replace($old,$default,$line) }
else { throw '发现自定义 SFTP 子系统，已保留原配置。' }
$changed = $next -cne $old
if ($changed -and (Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | Where-Object LocalPort -eq 22)) { throw '当前有 SSH 会话，暂缓重启。' }
[IO.Directory]::CreateDirectory($install) | Out-Null
Copy-Item -LiteralPath $source -Destination $helper -Force
$action = New-ScheduledTaskAction -Execute $helper -Argument '--prune'
$trigger = New-ScheduledTaskTrigger -Daily -At '00:05'
$principal = New-ScheduledTaskPrincipal -UserId $account -LogonType S4U -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'SshBeacon-PruneTransfers' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
if ($changed) {
    $backup = "$config.sshbeacon-$((Get-Date).ToString('yyyyMMdd-HHmmss')).bak"
    Copy-Item -LiteralPath $config -Destination $backup
    $restart = $false
    try {
        [IO.File]::WriteAllText($config,$next,(New-Object Text.UTF8Encoding($false)))
        & $server -t -f $config
        if ($LASTEXITCODE) { throw '新配置未通过 OpenSSH 检查。' }
        $restart = $true
        Restart-Service sshd -ErrorAction Stop
        (Get-Service sshd).WaitForStatus('Running',[TimeSpan]::FromSeconds(15))
    } catch {
        [IO.File]::WriteAllBytes($config,[IO.File]::ReadAllBytes($backup))
        if ($restart) { Restart-Service sshd }
        throw
    }
}
