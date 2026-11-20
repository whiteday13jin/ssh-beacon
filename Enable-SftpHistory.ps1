# 在管理员 PowerShell 中运行。保留现有 SSH 登录配置，仅调整默认 SFTP 子系统。
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:ProgramData 'ssh'
$config = Join-Path $base 'sshd_config'
$server = Join-Path $env:SystemRoot 'System32\OpenSSH\sshd.exe'
$line = 'Subsystem sftp sftp-server.exe -l INFO'
$old = [IO.File]::ReadAllText($config)
$pattern = '(?im)^[ \t]*Subsystem[ \t]+sftp[ \t]+(?:sftp-server\.exe(?:[ \t]+-l[ \t]+INFO)?|[^\r\n]*[\\/]SshBeacon[\\/]SftpAudit\.exe[ \t]+--account[ \t]+S-1-[0-9-]+)[ \t]*(?=\r?$)'
if ([regex]::Matches($old,$pattern).Count -ne 1) { throw '发现自定义 SFTP 子系统，已保留原配置。' }
$next = [regex]::Replace($old,$pattern,$line)
$changed = $next -cne $old
if ($changed -and (Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | Where-Object LocalPort -eq 22)) { throw '当前有 SSH 会话，暂缓重启。' }
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
