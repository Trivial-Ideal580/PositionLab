param([string]$OutputName = 'PositionLab.exe', [string]$Version = '', [string]$Pfx = '', [string]$PfxPassword = '', [string]$Timestamp = 'http://timestamp.digicert.com', [switch]$AllowUntrusted)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionFile = Join-Path $root 'VERSION'
    $Version = if (Test-Path -LiteralPath $versionFile) { (Get-Content -LiteralPath $versionFile -Raw).Trim() } else { '0.0.0' }
}
$text = ($Version -replace '[^0-9A-Za-z.\-+]', '').Trim()
if ($text.Length -eq 0) { throw '版本号无效。' }
$core = @($text.Split('.') | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })
while ($core.Count -lt 4) { $core += 0 }
$fileVersion = ($core[0..3] -join '.')
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
$obj = Join-Path $root 'obj'
New-Item -ItemType Directory -Force -Path $obj | Out-Null
$info = Join-Path $obj 'VersionInfo.cs'
$body = @"
using System.Reflection;
[assembly: AssemblyTitle("PositionLab")]
[assembly: AssemblyProduct("PositionLab")]
[assembly: AssemblyDescription("持仓分析与因子实验室")]
[assembly: AssemblyCompany("Trivial-Ideal580")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Trivial-Ideal580")]
[assembly: AssemblyVersion("$fileVersion")]
[assembly: AssemblyFileVersion("$fileVersion")]
[assembly: AssemblyInformationalVersion("$text")]
"@
[System.IO.File]::WriteAllText($info, $body, (New-Object System.Text.UTF8Encoding($true)))
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
$sources += $info
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Microsoft.VisualBasic.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll ('/out:' + (Join-Path $root $OutputName)) $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$target = Join-Path $root $OutputName
if ($Pfx) {
    if (!(Test-Path -LiteralPath $Pfx)) { throw "证书文件不存在：$Pfx" }
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Pfx, $PfxPassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::DefaultKeySet)
    if (!$cert.HasPrivateKey) { throw '证书缺少私钥，无法签名。' }
    $signature = Set-AuthenticodeSignature -FilePath $target -Certificate $cert -TimestampServer $Timestamp -HashAlgorithm SHA256
    Write-Host ("Signed: thumbprint={0} status={1}" -f $cert.Thumbprint, $signature.Status)
    if (-not $signature.SignerCertificate) { throw ("签名未写入文件：" + $signature.StatusMessage) }
    if (-not $signature.TimeStamperCertificate) { Write-Warning '未获得时间戳，证书到期后该签名将失效。' }
    if ($signature.Status -ne 'Valid') {
        if ($AllowUntrusted) { Write-Warning ("签名已写入，但证书链不受信任（开发证书属正常）：" + $signature.StatusMessage) }
        else { throw ("签名失败：" + $signature.StatusMessage) }
    }
}
Write-Host ("Built {0} version {1} (file {2})" -f $OutputName, $text, $fileVersion)
