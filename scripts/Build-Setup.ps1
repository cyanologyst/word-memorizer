[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = "1.0.0.0",
    [Parameter(Mandatory)]
    [string]$CertificateThumbprint,
    [Parameter(Mandatory)]
    [string]$MsiPath,
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\release",
    [string]$RuntimeInstallerUrl = "https://aka.ms/windowsappsdk/1.8/1.8.260529003/windowsappruntimeinstall-x64.exe",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$bundle = Join-Path $repoRoot "installer\Bundle.wxs"
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$msi = [System.IO.Path]::GetFullPath($MsiPath)
$cache = Join-Path $repoRoot "artifacts\prerequisites"
$runtimeInstaller = Join-Path $cache "WindowsAppRuntimeInstall-x64.exe"
$setupPath = Join-Path $output "WordReviewReminder-Setup-x64.exe"
$signingWork = Join-Path $repoRoot "artifacts\intermediate\setup-sign-$PID"
$unsignedSetup = Join-Path $signingWork "WordReviewReminder-Setup-x64.exe"
$signedEngine = Join-Path $signingWork "WordReviewReminder-Setup-x64.engine.exe"
$parsedVersion = [Version]$Version
$bundleVersion = "$($parsedVersion.Major).$($parsedVersion.Minor).$($parsedVersion.Build).$($parsedVersion.Revision)"

if (-not (Test-Path -LiteralPath $msi)) {
    throw "MSI package not found: $msi"
}

New-Item -ItemType Directory -Path $cache, $output, $signingWork -Force | Out-Null

if (-not (Test-Path -LiteralPath $runtimeInstaller)) {
    Invoke-WebRequest -Uri $RuntimeInstallerUrl -OutFile $runtimeInstaller -UseBasicParsing
}

$runtimeSignature = Get-AuthenticodeSignature -FilePath $runtimeInstaller
if ($null -eq $runtimeSignature.SignerCertificate -or
    $runtimeSignature.SignerCertificate.Subject -notlike "*Microsoft Corporation*") {
    throw "The Windows App Runtime prerequisite is not signed by Microsoft."
}

& dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "WiX tool restore failed with exit code $LASTEXITCODE."
}

& dotnet wix extension add -g WixToolset.BootstrapperApplications.wixext/6.0.2
if ($LASTEXITCODE -ne 0) {
    throw "WiX bootstrapper extension restore failed with exit code $LASTEXITCODE."
}

& dotnet wix build $bundle `
    -arch x64 `
    -d "BundleVersion=$bundleVersion" `
    -d "MsiPath=$msi" `
    -d "RuntimeInstaller=$runtimeInstaller" `
    -d "IconPath=$repoRoot\WordReviewReminder\Assets\AppIcon.ico" `
    -d "LogoPath=$repoRoot\WordReviewReminder\Assets\AppLogo.png" `
    -d "LicensePath=$repoRoot\installer\License.rtf" `
    -ext WixToolset.BootstrapperApplications.wixext `
    -culture en-US `
    -out $unsignedSetup
if ($LASTEXITCODE -ne 0) {
    throw "WiX setup bundle build failed with exit code $LASTEXITCODE."
}

$signTool = Get-ChildItem `
    -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" `
    -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $signTool) {
    throw "SignTool.exe was not found. Install the Windows SDK signing tools."
}

& dotnet wix burn detach $unsignedSetup -engine $signedEngine
if ($LASTEXITCODE -ne 0) {
    throw "Burn engine detach failed with exit code $LASTEXITCODE."
}

& $signTool.FullName sign `
    /fd SHA256 `
    /sha1 $CertificateThumbprint `
    /s My `
    /tr $TimestampUrl `
    /td SHA256 `
    /d "Word Review Reminder Setup" `
    $signedEngine
if ($LASTEXITCODE -ne 0) {
    throw "Burn engine signing failed with exit code $LASTEXITCODE."
}

$engineSignature = Get-AuthenticodeSignature -FilePath $signedEngine
if ($null -eq $engineSignature.SignerCertificate -or
    $engineSignature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
    throw "The detached Burn engine is missing the expected code signature."
}

& dotnet wix burn reattach $unsignedSetup -engine $signedEngine -o $setupPath
if ($LASTEXITCODE -ne 0) {
    throw "Burn engine reattach failed with exit code $LASTEXITCODE."
}

& $signTool.FullName sign `
    /fd SHA256 `
    /sha1 $CertificateThumbprint `
    /s My `
    /tr $TimestampUrl `
    /td SHA256 `
    /d "Word Review Reminder Setup" `
    $setupPath
if ($LASTEXITCODE -ne 0) {
    throw "Setup bundle signing failed with exit code $LASTEXITCODE."
}

$signature = Get-AuthenticodeSignature -FilePath $setupPath
if ($null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
    throw "The generated setup is missing the expected code signature."
}

Write-Host "Signed setup ready: $setupPath"
Get-Item -LiteralPath $setupPath | Select-Object Name, Length, LastWriteTime
