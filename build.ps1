# 用 Windows 自带的 .NET Framework 编译器生成 SshBeacon.exe
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$a = @('/nologo', '/target:winexe', '/main:SshBeacon.Program', '/codepage:65001', '/optimize+', '/platform:anycpu',
    '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    "/out:$dir\SshBeacon.exe", "$dir\SshBeacon.cs", "$dir\BeaconVisuals.cs", "$dir\ActivityWindow.cs", "$dir\AccessHistory.cs", "$dir\SftpAudit.cs")
& $csc $a
if ($LASTEXITCODE -ne 0) { throw "compile failed: $LASTEXITCODE" }
"built: $dir\SshBeacon.exe"

& $csc /nologo /target:exe /platform:x64 /codepage:65001 "/out:$dir\SftpAudit.exe" "$dir\SftpAudit.cs"
if ($LASTEXITCODE -ne 0) { throw 'SFTP helper build failed' }