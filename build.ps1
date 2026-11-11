# 用 Windows 自带的 .NET Framework 编译器生成 SshBeacon.exe
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$a = @('/nologo', '/target:winexe', '/codepage:65001', '/optimize+', '/platform:anycpu',
    '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    "/out:$dir\SshBeacon.exe", "$dir\SshBeacon.cs")
& $csc $a
if ($LASTEXITCODE -ne 0) { throw "compile failed: $LASTEXITCODE" }
"built: $dir\SshBeacon.exe"
