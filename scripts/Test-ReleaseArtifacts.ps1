[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$ReleaseDirectory = "$PSScriptRoot\..\artifacts\release",
    [string]$CertificateThumbprint
)

$ErrorActionPreference = "Stop"
$release = [System.IO.Path]::GetFullPath($ReleaseDirectory)
$requiredFiles = @(
    "WordReviewReminder-Setup-x64.exe",
    "WordReviewReminder-x64.msi",
    "WordReviewReminder-x64.msix",
    "WordReviewReminder.appinstaller",
    "WordReviewReminder.cer",
    "SHA256SUMS.txt"
)

foreach ($name in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $release $name))) {
        throw "Required release artifact is missing: $name"
    }
}

$expectedHashes = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $release "SHA256SUMS.txt")) {
    if ($line -match '^([0-9a-f]{64})\s+(.+)$') {
        $expectedHashes[$matches[2]] = $matches[1]
    }
}

foreach ($name in $requiredFiles | Where-Object { $_ -notlike "*.txt" }) {
    if (-not $expectedHashes.ContainsKey($name)) {
        throw "Checksum manifest does not include $name."
    }

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $release $name)).Hash.ToLowerInvariant()
    if ($actual -ne $expectedHashes[$name]) {
        throw "Checksum mismatch for $name."
    }
}

$signedFiles = @(
    "WordReviewReminder-Setup-x64.exe",
    "WordReviewReminder-x64.msi",
    "WordReviewReminder-x64.msix"
)
foreach ($name in $signedFiles) {
    $signature = Get-AuthenticodeSignature -FilePath (Join-Path $release $name)
    if ($null -eq $signature.SignerCertificate) {
        throw "$name does not have an Authenticode signer."
    }
    if ($CertificateThumbprint -and $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
        throw "$name was not signed by the expected certificate."
    }
}

$msiPath = Join-Path $release "WordReviewReminder-x64.msi"
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msiPath, 0)
$view = $database.OpenView("SELECT Value FROM Property WHERE Property='ProductVersion'")
$view.Execute()
$record = $view.Fetch()
$msiVersion = $record.StringData(1)
$expectedMsiVersion = ([Version]$Version).ToString(3)
if ($msiVersion -ne $expectedMsiVersion) {
    throw "MSI version is $msiVersion; expected $expectedMsiVersion."
}

[xml]$appInstaller = Get-Content -Raw -LiteralPath (Join-Path $release "WordReviewReminder.appinstaller")
if ($appInstaller.AppInstaller.Version -ne $Version) {
    throw "App Installer version is $($appInstaller.AppInstaller.Version); expected $Version."
}

$makeAppx = Get-ChildItem `
    -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" `
    -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $makeAppx) {
    throw "MakeAppx.exe was not found."
}

$verificationRoot = Join-Path $env:TEMP "WordReviewReminder-release-verify-$PID"
$msixExtract = Join-Path $verificationRoot "msix"
New-Item -ItemType Directory -Path $msixExtract -Force | Out-Null

& $makeAppx.FullName unpack `
    /p (Join-Path $release "WordReviewReminder-x64.msix") `
    /d $msixExtract `
    /o | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "MSIX extraction failed with exit code $LASTEXITCODE."
}

[xml]$manifest = Get-Content -Raw -LiteralPath (Join-Path $msixExtract "AppxManifest.xml")
if ($manifest.Package.Identity.Version -ne $Version) {
    throw "MSIX identity version is $($manifest.Package.Identity.Version); expected $Version."
}

Write-Host "Release verification passed for version $Version."
Get-ChildItem -LiteralPath $release -File | Select-Object Name, Length, LastWriteTime
