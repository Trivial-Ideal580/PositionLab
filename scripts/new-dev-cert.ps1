param([string]$Password = 'positionlab', [string]$Subject = 'CN=Trivial-Ideal580 PositionLab (development)')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root 'certs'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -KeyUsage DigitalSignature `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(3)
}
$pfx = Join-Path $dir 'dev-signing.pfx'
$secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
Write-Host "自签名证书已就绪（仅供开发，不会被 Windows 信任）"
Write-Host "  指纹 = $($cert.Thumbprint)"
Write-Host "  PFX  = $pfx"
Write-Host "  密码 = $Password"
Write-Host "  清理 = Remove-Item Cert:\CurrentUser\My\$($cert.Thumbprint)"
