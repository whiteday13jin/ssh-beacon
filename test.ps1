param([switch]$PreviewSheet)
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $env:TEMP ('SshBeacon.VisualTests.' + [guid]::NewGuid().ToString('N') + '.exe')
Push-Location $dir
try {
    & $csc /nologo /target:exe /codepage:65001 /main:SshBeacon.VisualTests /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$out" "$dir\SshBeacon.cs" "$dir\BeaconVisuals.cs" "$dir\ActivityWindow.cs" "$dir\AccessHistory.cs" "$dir\SftpAudit.cs" "$dir\tests\VisualTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    if ($PreviewSheet) { & $out --preview-sheet } else { & $out }
    if ($LASTEXITCODE -ne 0) { throw 'Visual tests failed' }
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out }
}
