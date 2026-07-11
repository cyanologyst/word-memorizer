[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = "1.0.0.0",
    [string]$CertificatePath = "$PSScriptRoot\..\installer\certs\WordReviewReminder.pfx",
    [Parameter(Mandatory)]
    [string]$CertificatePassword,
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\release"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$project = Join-Path $repoRoot "WordReviewReminder\WordReviewReminder.csproj"
$certificate = [System.IO.Path]::GetFullPath($CertificatePath)
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$template = Join-Path $repoRoot "installer\WordReviewReminder.appinstaller.template"

if (-not (Test-Path -LiteralPath $certificate)) {
    throw "Signing certificate not found: $certificate"
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$buildStarted = Get-Date
$securePassword = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
$signingCertificate = Import-PfxCertificate `
    -FilePath $certificate `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -Password $securePassword

if ($null -eq $signingCertificate -or [string]::IsNullOrWhiteSpace($signingCertificate.Thumbprint)) {
    throw "The signing certificate could not be imported."
}

$arguments = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:Platform=x64",
    "-p:WindowsPackageType=MSIX",
    "-p:WindowsAppSDKSelfContained=true",
    "-p:GenerateAppxPackageOnBuild=true",
    "-p:AppxPackageSigningEnabled=true",
    "-p:PackageCertificateThumbprint=$($signingCertificate.Thumbprint)",
    "-p:AppxPackageVersion=$Version",
    "-p:AppxBundle=Never",
    "-p:AppxSymbolPackageEnabled=false",
    "-p:DebugSymbols=false",
    "-p:DebugType=None",
    "-p:UapAppxPackageBuildMode=SideloadOnly"
)

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "MSIX publish failed with exit code $LASTEXITCODE."
}

$package = Get-ChildItem -Path (Split-Path $project) -Recurse -Filter "*.msix" |
    Where-Object { $_.LastWriteTime -ge $buildStarted.AddSeconds(-2) } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $package) {
    throw "The build succeeded but no new MSIX package was found."
}

$msixPath = Join-Path $output "WordReviewReminder-x64.msix"
Copy-Item -LiteralPath $package.FullName -Destination $msixPath -Force

$signature = Get-AuthenticodeSignature -FilePath $msixPath
if ($null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $signingCertificate.Thumbprint) {
    throw "The generated MSIX is missing the expected code signature."
}

$appInstaller = (Get-Content -Raw -LiteralPath $template).Replace("__VERSION__", $Version)
$appInstallerPath = Join-Path $output "WordReviewReminder.appinstaller"
[System.IO.File]::WriteAllText($appInstallerPath, $appInstaller, [System.Text.UTF8Encoding]::new($false))

$cerPath = Join-Path $output "WordReviewReminder.cer"
Export-Certificate -Cert $signingCertificate -FilePath $cerPath -Force | Out-Null

$checksums = Get-FileHash -Algorithm SHA256 -LiteralPath $msixPath, $appInstallerPath, $cerPath
$checksumPath = Join-Path $output "SHA256SUMS.txt"
$checksumLines = $checksums | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)" }
[System.IO.File]::WriteAllLines($checksumPath, $checksumLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "Release artifacts are ready in $output"
Get-ChildItem -LiteralPath $output | Select-Object Name, Length, LastWriteTime
