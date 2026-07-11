[CmdletBinding()]
param(
    [string]$OutputDirectory = "$PSScriptRoot\..\installer\certs",
    [string]$Publisher = "CN=cyanologyst",
    [Parameter(Mandatory)]
    [string]$Password
)

$ErrorActionPreference = "Stop"
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null

$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Publisher `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(5)

$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
$pfxPath = Join-Path $output "WordReviewReminder.pfx"
$cerPath = Join-Path $output "WordReviewReminder.cer"

Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null

Write-Host "Created development signing certificate:"
Write-Host "  PFX: $pfxPath"
Write-Host "  CER: $cerPath"
Write-Host "Keep the PFX and password private. Distribute only the CER when using a self-signed build."
